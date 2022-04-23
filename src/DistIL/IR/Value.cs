namespace DistIL.IR;

public abstract class Value
{
    public RType ResultType { get; set; } = PrimType.Void;
    /// <summary> Whether this value's result type is not void. </summary>
    public bool HasResult => ResultType.Kind != TypeKind.Void;

    /// <summary> List of instruction operands using this value. </summary>
    public List<Use> Uses { get; } = new();

    internal void AddUse(Instruction user, int operandIdx)
    {
        Assert(!RemoveUse(user, operandIdx));
        Assert(GetType() != typeof(Variable) || user is LoadVarInst or StoreVarInst);

        Uses.Add(new() { 
            Inst = user, 
            OperandIdx = operandIdx
        });
    }
    internal bool RemoveUse(Instruction user, int operandIdx)
    {
        for (int i = 0; i < Uses.Count; i++) {
            var use = Uses[i];
            if (use.Inst == user && use.OperandIdx == operandIdx) {
                Uses.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary> Replace uses of this value with `newValue`. Use list is cleared on return. </summary>
    public void ReplaceUses(Value newValue)
    {
        if (newValue == this) return;

        foreach (var (inst, operIdx) in Uses) {
            Assert(inst.Operands[operIdx] == this);

            inst.Operands[operIdx] = newValue;
            newValue.AddUse(inst, operIdx);
        }
        Uses.Clear();
    }

    /// <summary> Replace each use of this value with the value returned by `getNewValueForUser`. Use list is cleared on return. </summary>
    public void ReplaceUses(Func<Instruction, Value> getNewValueForUser)
    {
        for (int i = 0; i < Uses.Count; i++) {
            var (inst, operIdx) = Uses[i];
            Assert(inst.Operands[operIdx] == this);

            var newValue = getNewValueForUser(inst);
            Assert(newValue != this); //not impl

            inst.Operands[operIdx] = newValue;
            newValue.AddUse(inst, operIdx);
        }
        Uses.Clear();
    }

    /// <summary> Returns the instruction using this value at the specified use index. Equivalent to `Uses[index].Inst` </summary>
    public Instruction GetUse(int index)
    {
        return Uses[index].Inst;
    }

    public abstract void Print(StringBuilder sb, SlotTracker slotTracker);
    public virtual void PrintAsOperand(StringBuilder sb, SlotTracker slotTracker) => Print(sb, slotTracker);
    protected virtual SlotTracker GetDefaultSlotTracker() => new();

    public override string ToString()
    {
        var sb = new StringBuilder();
        Print(sb, GetDefaultSlotTracker());
        return sb.ToString();
    }
}

public struct Use
{
    public Instruction Inst { get; init; }
    public int OperandIdx { get; init; }

    public void Deconstruct(out Instruction inst, out int operandIdx)
        => (inst, operandIdx) = (Inst, OperandIdx);

    public static implicit operator Instruction(Use use) => use.Inst;

    public override string ToString() => Inst.ToString();
}