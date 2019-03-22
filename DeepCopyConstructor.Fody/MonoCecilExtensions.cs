using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace DeepCopyConstructor.Fody
{
    public static class Extensions
    {
        public static bool HasCopyConstructor(this TypeDefinition type, out MethodReference constructor)
        {
            constructor = type.GetConstructors()
                .Where(c => c.Parameters.Count == 1)
                .SingleOrDefault(c => c.Parameters.Single().ParameterType.FullName == type.FullName);
            return constructor != null;
        }

        public static bool HasDeepCopyConstructorAttribute(this ICustomAttributeProvider type)
            => type.CustomAttributes.Any(a => a.AttributeType.FullName == ModuleWeaver.DeepCopyConstructorAttribute);

        public static bool IsImplementing(this TypeReference type, string typeFullName)
        {
            while (type != null)
            {
                if (type.GetElementType().FullName == typeFullName)
                    return true;

                var def = type.Resolve();
                if (def.Interfaces.Any(i => i.InterfaceType.IsImplementing(typeFullName)))
                    return true;

                type = def.BaseType;
            }

            return false;
        }
        
        public static MethodReference GetMethod(this TypeDefinition type, string name)
        {
            if (TryFindMethod(type, name, out var method))
                return method;

            throw new NullReferenceException($"No method {name} found for type {type.FullName}");
        }

        private static bool TryFindMethod(this TypeDefinition type, string name, out MethodReference method)
        {
            var current = type;
            do
            {
                method = current.Methods.SingleOrDefault(m => m.Name == name);
                if (method != null)
                    return true;
                foreach (var @interface in current.Interfaces)
                    if (TryFindMethod(@interface.InterfaceType.Resolve(), name, out var interfaceMethod))
                    {
                        method = interfaceMethod;
                        return true;
                    }

                current = current.BaseType?.Resolve();
            } while (current != null);

            method = null;
            return false;
        }

        public static TypeReference SingleGenericArgument(this TypeReference type)
        {
            return ((GenericInstanceType) type).GenericArguments.Single().GetElementType();
        }

        public static TypeReference MakeGeneric(this TypeReference source, params TypeReference[] arguments)
        {
            if (source.GenericParameters.Count != arguments.Length)
                throw new ArgumentException();

            var instance = new GenericInstanceType(source);
            foreach (var argument in arguments)
                instance.GenericArguments.Add(argument);

            return instance;
        }

        public static MethodReference MakeGeneric(this MethodReference source, params TypeReference[] arguments)
        {
            var reference = new MethodReference(source.Name, source.ReturnType)
            {
                DeclaringType = source.DeclaringType.MakeGeneric(arguments),
                HasThis = source.HasThis,
                ExplicitThis = source.ExplicitThis,
                CallingConvention = source.CallingConvention,
            };

            foreach (var parameter in source.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (var genericParameter in source.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));

            return reference;
        }
    }
}