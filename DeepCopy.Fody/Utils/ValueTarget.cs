using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DeepCopy.Fody.Utils
{
    public class ValueTarget : IDisposable
    {
        public static ValueTarget New()
        {
            return new ValueTarget();
        }

        private PropertyDefinition _property;
        private VariableDefinition _instance;
        private VariableDefinition _variable;
        private VariableDefinition _index;
        private MethodReference _call;
        private MethodReference _callvirt;

        private ICollection<Instruction> _instructions;
        private Instruction _next;

        public bool IsTargetingBase => _property == null && _instance == null && _variable == null;

        private readonly IList<OpCode> _added = new List<OpCode>();

        private ValueTarget() { }

        public ValueTarget Property(PropertyDefinition property)
        {
            _property = property;
            return this;
        }

        public ValueTarget Instance(VariableDefinition instance)
        {
            _instance = instance;
            return this;
        }

        public ValueTarget Variable(VariableDefinition variable)
        {
            _variable = variable;
            return this;
        }

        public ValueTarget Index(VariableDefinition index)
        {
            _index = index;
            return this;
        }

        public ValueTarget Call(MethodReference method)
        {
            _call = method;
            return this;
        }

        public ValueTarget Callvirt(MethodReference method)
        {
            _callvirt = method;
            return this;
        }

        public ValueTarget Add(OpCode code)
        {
            _added.Add(code);
            return this;
        }

        public IEnumerable<Instruction> Build(VariableDefinition variable) => Build(ValueSource.New().Variable(variable));

        public IEnumerable<Instruction> Build(ValueSource source)
        {
            var instructions = new List<Instruction>();
            using (Build(instructions))
                instructions.AddRange(source);
            return instructions;
        }

        public IDisposable Build(ICollection<Instruction> instructions)
        {
            _instructions = instructions;
            if (_variable == null)
                instructions.Add(_instance != null
                    ? Instruction.Create(OpCodes.Ldloc, _instance)
                    : Instruction.Create(OpCodes.Ldarg_0));

            if (_index != null)
            {
                if (_variable != null)
                    _instructions.Add(_variable.CreateLoadInstruction());
                else if (_property != null)
                    instructions.Add(_property.CreateGetInstruction());
                _instructions.Add(Instruction.Create(OpCodes.Ldloc, _index));

                return this;
            }

            return this;
        }

        public IDisposable Build(ICollection<Instruction> instructions, out Instruction next)
        {
            Build(instructions);

            _next = Instruction.Create(OpCodes.Nop);
            next = _next;
            return this;
        }

        public void Dispose()
        {
            if (_instructions == null)
                throw new InvalidOperationException();

            if (_next != null)
                _instructions.Add(_next);

            if (_index != null)
                _instructions.Add(Instruction.Create(OpCodes.Stelem_Ref));
            else if (_property != null)
                _instructions.Add(_property.CreateSetInstruction());
            else if (_call != null)
                _instructions.Add(Instruction.Create(OpCodes.Call, _call));
            else if (_callvirt != null)
                _instructions.Add(Instruction.Create(OpCodes.Callvirt, _callvirt));
            else if (_variable != null)
                _instructions.Add(Instruction.Create(OpCodes.Stloc, _variable));

            foreach (var code in _added)
                _instructions.Add(Instruction.Create(code));

            _instructions = null;
            _next = null;
        }
    }
}