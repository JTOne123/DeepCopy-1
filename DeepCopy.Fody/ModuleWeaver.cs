﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DeepCopy.Fody.Utils;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

#region ModuleWeaver

namespace DeepCopy.Fody
{
    public partial class ModuleWeaver : BaseModuleWeaver
    {
        private const string ConstructorName = ".ctor";
        private const string DeepCopyExtensionAttribute = "DeepCopy.DeepCopyExtensionAttribute";
        private const string AddDeepCopyConstructorAttribute = "DeepCopy.AddDeepCopyConstructorAttribute";
        private const string InjectDeepCopyAttribute = "DeepCopy.InjectDeepCopyAttribute";
        private const string IgnoreDuringDeepCopyAttribute = "DeepCopy.IgnoreDuringDeepCopyAttribute";
        private const string DeepCopyByReferenceAttribute = "DeepCopy.DeepCopyByReferenceAttribute";

        private const MethodAttributes ConstructorAttributes
            = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;

        internal ThreadLocal<MethodBody> CurrentBody { get; } = new ThreadLocal<MethodBody>();
        private IDictionary<MetadataToken, TypeDefinition> AddDeepCopyConstructorTargets { get; } = new Dictionary<MetadataToken, TypeDefinition>();
        private IDictionary<MetadataToken, MethodReference> DeepCopyExtensions { get; } = new Dictionary<MetadataToken, MethodReference>();

        public override void Execute()
        {
            ExecuteDeepCopyExtensions();
            ExecuteAddDeepCopyConstructor();
            ExecuteInjectDeepCopy();
        }

        private void ExecuteDeepCopyExtensions()
        {
            foreach (var method in ModuleDefinition.Types.WithNestedTypes().SelectMany(t => t.Methods).Where(m => m.AnyAttribute(DeepCopyExtensionAttribute)))
            {
                var attribute = method.SingleAttribute(DeepCopyExtensionAttribute);
                InjectDeepCopyExtension(method, attribute);
                method.CustomAttributes.Remove(attribute);
            }
        }

        private void ExecuteAddDeepCopyConstructor()
        {
            foreach (var target in AddDeepCopyConstructorTargets.Values)
            {
                if (target.HasCopyConstructor(out _))
                    throw new DeepCopyException($"{target.FullName} has own copy constructor. Use [InjectDeepCopy] on constructor if needed");

                AddDeepConstructor(target);
            }

            foreach (var target in ModuleDefinition.Types.WithNestedTypes())
            {
                if (!target.AnyAttribute(AddDeepCopyConstructorAttribute))
                    continue;

                if (target.HasCopyConstructor(out _))
                    throw new DeepCopyException($"{target.FullName} has own copy constructor. Use [InjectDeepCopy] on constructor if needed");

                AddDeepConstructor(target);
                target.CustomAttributes.Remove(target.SingleAttribute(AddDeepCopyConstructorAttribute));
            }
        }

        private void ExecuteInjectDeepCopy()
        {
            foreach (var target in ModuleDefinition.Types.Where(t => t.GetConstructors().Any(c => c.AnyAttribute(InjectDeepCopyAttribute))))
            {
                var constructors = target.GetConstructors().Where(c => c.AnyAttribute(InjectDeepCopyAttribute)).ToList();
                if (constructors.Count > 1)
                    throw new DeepCopyException($"{target.FullName} multiple constructors marked with [InjectDeepCopy]");
                var constructor = constructors.Single();
                if (constructor.Parameters.Count != 1
                    || constructor.Parameters.Single().ParameterType.Resolve().MetadataToken != target.Resolve().MetadataToken)
                    throw new DeepCopyException($"Constructor {constructor} is no copy constructor");

                var constructorResolved = constructor.Resolve();
                constructorResolved.Body.SimplifyMacros();
                InsertCopyInstructions(target, constructorResolved.Body, null);
                constructorResolved.CustomAttributes.Remove(constructorResolved.SingleAttribute(InjectDeepCopyAttribute));
            }
        }

        private void AddDeepConstructor(TypeDefinition type)
        {
            var constructor = new MethodDefinition(ConstructorName, ConstructorAttributes, TypeSystem.VoidReference);
            constructor.Parameters.Add(new ParameterDefinition(type));

            var processor = constructor.Body.GetILProcessor();

            Func<TypeReference, IEnumerable<Instruction>> baseCopyFunc = null;

            if (type.BaseType.Resolve().MetadataToken == TypeSystem.ObjectDefinition.MetadataToken)
            {
                processor.Emit(OpCodes.Ldarg_0);
                processor.Emit(OpCodes.Call, ImportDefaultConstructor(TypeSystem.ObjectDefinition));
            }
            else if (IsCopyConstructorAvailable(type.BaseType, out var baseConstructor))
            {
                processor.Emit(OpCodes.Ldarg_0);
                processor.Emit(OpCodes.Ldarg_1);
                processor.Emit(OpCodes.Call, baseConstructor);
            }
            else if (IsType(type.BaseType.GetElementType().Resolve(), typeof(Dictionary<,>)))
            {
                processor.Emit(OpCodes.Ldarg_0);
                processor.Emit(OpCodes.Call, ImportDefaultConstructor(type.BaseType));
                baseCopyFunc = reference => CopyDictionary(reference, ValueSource.New(), ValueTarget.New());
            }
            else if (IsType(type.BaseType.GetElementType().Resolve(), typeof(List<>)))
            {
                processor.Emit(OpCodes.Ldarg_0);
                processor.Emit(OpCodes.Call, ImportDefaultConstructor(type.BaseType));
                baseCopyFunc = reference => CopyList(reference, ValueSource.New(), ValueTarget.New());
            }
            else if (IsType(type.BaseType.GetElementType().Resolve(), typeof(HashSet<>)))
            {
                processor.Emit(OpCodes.Ldarg_0);
                processor.Emit(OpCodes.Call, ImportDefaultConstructor(type.BaseType));
                baseCopyFunc = reference => CopySet(reference, ValueSource.New(), ValueTarget.New());
            }
            else
                throw new NoCopyConstructorFoundException(type.BaseType);

            InsertCopyInstructions(type, constructor.Body, baseCopyFunc);

            processor.Emit(OpCodes.Ret);
            type.Methods.Add(constructor);
        }

        private void InsertCopyInstructions(TypeDefinition type, MethodBody body, Func<TypeReference, IEnumerable<Instruction>> baseCopyInstruction)
        {
            try
            {
                CurrentBody.Value = body;

                var baseConstructorCall = body.Instructions.Single(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference method && method.Name == ConstructorName);
                var index = body.Instructions.IndexOf(baseConstructorCall) + 1;
                var properties = new List<string>();

                if (baseCopyInstruction != null)
                    foreach (var instruction in baseCopyInstruction.Invoke(type.BaseType))
                        body.Instructions.Insert(index++, instruction);

                foreach (var property in type.Properties)
                {
                    if (!TryCopy(property, out var instructions))
                        continue;
                    properties.Add(property.Name);
                    foreach (var instruction in instructions)
                        body.Instructions.Insert(index++, instruction);
                }

                WriteInfo($"{type.FullName} -> {(properties.Count == 0 ? "no properties" : string.Join(", ", properties))}");

                if (body.HasVariables)
                    body.InitLocals = true;

                body.OptimizeMacros();
            }
            catch (DeepCopyException exception)
            {
                exception.ProcessingType = type;
                throw;
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                throw new DeepCopyException(exception.Message) { ProcessingType = type };
            }
            finally
            {
                CurrentBody.Value = null;
            }
        }

        #region Setup

        public override bool ShouldCleanReference => true;

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            yield return "netstandard";
            yield return "mscorlib";
        }

        #endregion
    }
}

#endregion