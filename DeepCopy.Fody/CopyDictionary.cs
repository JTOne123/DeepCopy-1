using System.Collections.Generic;
using DeepCopy.Fody.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DeepCopy.Fody
{
    public partial class ModuleWeaver
    {
        private IEnumerable<Instruction> CopyDictionary(TypeReference type, ValueSource source, ValueTarget target)
        {
            var typesOfArguments = type.GetGenericArguments();
            var typeKeyValuePair = ImportType(typeof(KeyValuePair<,>), typesOfArguments);

            var list = new List<Instruction>();
            using (new IfNotNull(list, source, target.IsTargetingBase))
            {
                VariableDefinition variable = null;
                if (!target.IsTargetingBase)
                    list.AddRange(NewInstance(type, typeof(IDictionary<,>), typeof(Dictionary<,>), out variable));

                using (var forEach = new ForEach(this, list, type, source))
                {
                    var sourceKey = ValueSource.New().Variable(forEach.Current).Method(ImportMethod(typeKeyValuePair, "get_Key", typesOfArguments));
                    var sourceValue = ValueSource.New().Variable(forEach.Current).Method(ImportMethod(typeKeyValuePair, "get_Value", typesOfArguments));

                    var targetKey = NewVariable(typesOfArguments[0]);
                    list.AddRange(Copy(typesOfArguments[0], sourceKey, ValueTarget.New().Variable(targetKey)));
                    var targetValue = NewVariable(typesOfArguments[1]);
                    list.AddRange(Copy(typesOfArguments[1], sourceValue, ValueTarget.New().Variable(targetValue)));

                    list.Add(variable?.CreateLoadInstruction() ?? Instruction.Create(OpCodes.Ldarg_0));
                    list.Add(targetKey.CreateLoadInstruction());
                    list.Add(targetValue.CreateLoadInstruction());
                    list.Add(Instruction.Create(OpCodes.Callvirt, ImportMethod(type.Resolve(), "set_Item", typesOfArguments)));
                }

                if (!target.IsTargetingBase)
                    list.AddRange(target.Build(ValueSource.New().Variable(variable)));
            }

            return list;
        }
    }
}