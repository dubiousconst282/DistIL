namespace DistIL.IR;

public abstract class Instruction : Value
{
    public BasicBlock Block { get; set; } = null!;

    /// <summary> The previous instruction in the block. </summary>
    public Instruction? Prev { get; set; }
    /// <summary> The next instruction in the block. </summary>
    public Instruction? Next { get; set; }

    public Value[] Operands { get; set; }

    public int ILOffset { get; set; } = -1;

    /// <summary> Index of this instruction in the parent basic block. Only valid if `Block.OrderValid == true` </summary>
    public int Order { get; set; }

    public abstract string InstName { get; }

    /// <summary> Whether this instructions modifies global state, affects control flow, or throws exceptions. </summary>
    public virtual bool HasSideEffects => IsBranch || MayThrow;
    public virtual bool MayThrow => false;
    /// <summary> Whether this instruction is a branch, return, or affects control flow. </summary>
    public virtual bool IsBranch => false;
    /// <summary> Whether this instruction can be safely removed if it has no uses. </summary>
    public virtual bool SafeToRemove => !HasSideEffects;

    protected Instruction()
    {
        Operands = Array.Empty<Value>();
    }
    protected Instruction(params Value[] opers)
    {
        Operands = opers;

        for (int i = 0; i < opers.Length; i++) {
            opers[i].AddUse(this, i);
        }
    }

    /// <summary> Inserts this instruction before `inst`. </summary>
    public void InsertBefore(Instruction inst) => inst.Block.InsertBefore(inst, this);

    /// <summary> Inserts this instruction after `inst`. </summary>
    public void InsertAfter(Instruction inst) => inst.Block.InsertAfter(inst, this);

    /// <summary> 
    /// Removes this instruction from the parent block and replaces its uses with the specified value. 
    /// If `newValue` is an instruction with no parent block and `insertIfInst == true`, it will be added 
    /// in the same place as this instruction. 
    /// Operands of this instruction are keept unmodified, but uses are removed. 
    /// </summary>
    public void ReplaceWith(Value newValue, bool insertIfInst = true)
    {
        if (insertIfInst && newValue is Instruction newInst && newInst.Block == null) {
            Block.InsertAfter(this, newInst);
        }
        ReplaceUses(newValue);
        Remove();
    }

    /// <summary> Removes this instruction from the parent basic block. Operand uses are removed. </summary>
    public void Remove()
    {
        Block?.Remove(this);
        RemoveOperandUses();
    }

    internal void RemoveOperandUses()
    {
        for (int i = 0; i < Operands.Length; i++) {
            Operands[i].RemoveUse(this, i);
        }
    }

    /// <summary> Replaces the operand `prevOper` with `newOper`. </summary>
    public bool ReplaceOperand(Value prevOper, Value newOper)
    {
        for (int i = 0; i < Operands.Length; i++) {
            if (Operands[i] == prevOper) {
                ReplaceOperand(i, newOper);
                return true;
            }
        }
        return false;
    }
    /// <summary> Replaces the operand at `operIndex` with `newOper`. </summary>
    public void ReplaceOperand(int operIndex, Value newOper)
    {
        var prevOper = Operands[operIndex];
        if (newOper != prevOper) {
            prevOper?.RemoveUse(this, operIndex);
            Operands[operIndex] = newOper;
            newOper.AddUse(this, operIndex);
        }
    }

    /// <summary> 
    /// Extends the operand array by `amount` and returns the index of the first new element. 
    /// Newly allocated elements are set to null, they should be initialized immediately after calling this. 
    /// </summary>
    protected int GrowOperands(int amount)
    {
        var arr = Operands;
        int arrLen = arr.Length;
        Array.Resize(ref arr, arrLen + amount);
        Operands = arr;

        return arrLen;
    }
    /// <summary> Removes operands in the specified range. </summary>
    protected void RemoveOperands(int startIndex, int count)
    {
        Assert(startIndex >= 0 && startIndex + count <= Operands.Length);

        var oldArr = Operands;
        var newArr = new Value[Operands.Length - count];

        if (startIndex > 0) {
            Array.Copy(oldArr, 0, newArr, 0, startIndex);
        }
        for (int i = startIndex; i < startIndex + count; i++) {
            oldArr[i].RemoveUse(this, i);
        }
        for (int i = startIndex + count; i < oldArr.Length; i++) {
            oldArr[i].RelocUse(this, i, i - count);
            newArr[i - count] = oldArr[i];
        }
        Operands = newArr;
    }

    public abstract void Accept(InstVisitor visitor);

    public override void PrintAsOperand(StringBuilder sb, SlotTracker slotTracker)
    {
        int? id = slotTracker.GetId(this);
        sb.Append(id == null ? "r?" : "r" + id);
    }
    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        PrintPrefix(sb, slotTracker);
        PrintOperands(sb, slotTracker);
    }
    /// <summary> Prints the result variable and instruction name: [resultType operandName = ] instName </summary>
    protected virtual void PrintPrefix(StringBuilder sb, SlotTracker slotTracker)
    {
        if (HasResult) {
            sb.Append(ResultType);
            sb.Append(" ");
            PrintAsOperand(sb, slotTracker);
            sb.Append(" = ");
        }
        sb.Append(InstName);
    }
    /// <summary> Prints the instruction operands. </summary>
    protected virtual void PrintOperands(StringBuilder sb, SlotTracker slotTracker)
    {
        for (int i = 0; i < Operands.Length; i++) {
            sb.Append(i == 0 ? " " : ", ");
            Operands[i].PrintAsOperand(sb, slotTracker);
        }
    }
    protected override SlotTracker GetDefaultSlotTracker()
    {
        return Block?.Method.GetSlotTracker() ?? base.GetDefaultSlotTracker();
    }
}