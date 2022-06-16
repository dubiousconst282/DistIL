namespace DistIL.IR;

public abstract class Instruction : TrackedValue
{
    public BasicBlock Block { get; set; } = null!;

    /// <summary> The previous instruction in the block. </summary>
    public Instruction? Prev { get; set; }
    /// <summary> The next instruction in the block. </summary>
    public Instruction? Next { get; set; }

    internal Value[] _operands;
    internal Use[] _operandUses;
    public ReadOnlySpan<Value> Operands => _operands;

    public abstract string InstName { get; }

    /// <summary> Whether this instructions modifies global state, affects control flow, or throws exceptions. </summary>
    public virtual bool HasSideEffects => IsBranch || MayThrow;
    public virtual bool MayThrow => false;
    /// <summary> Whether this instruction is a branch, return, or affects control flow. </summary>
    public virtual bool IsBranch => false;
    /// <summary> Whether this instruction can be safely removed if it has no uses. </summary>
    public virtual bool SafeToRemove => !HasSideEffects;
    /// <summary> Whether this instruction must be on the start of a block (it's a PhiInst or GuardInst). </summary>
    public virtual bool IsHeader => false;

    protected Instruction()
    {
        _operands = Array.Empty<Value>();
        _operandUses = Array.Empty<Use>();
    }
    protected Instruction(params Value[] opers)
    {
        _operands = opers;
        _operandUses = new Use[opers.Length];

        for (int i = 0; i < opers.Length; i++) {
            _operandUses[i] = new Use() { User = this, OperIdx = i };
            _operands[i].AddUse(_operandUses[i]);
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

    /// <summary> Removes this instruction from the parent basic block. </summary>
    /// <remarks> 
    /// This method will remove uses from operands, while keeping the references (Operands array) intact.
    /// It should not be added in a block again after calling this.
    /// </remarks>
    public void Remove()
    {
        Block?.Remove(this);
        RemoveOperandUses();
    }

    internal void RemoveOperandUses()
    {
        for (int i = 0; i < _operands.Length; i++) {
            _operands[i].RemoveUse(_operandUses[i]);
        }
    }

    /// <summary> Replaces operands set to `oldValue` with `oldValue`. </summary>
    public void ReplaceOperands(Value oldValue, Value newValue)
    {
        for (int i = 0; i < _operands.Length; i++) {
            if (_operands[i] == oldValue) {
                _operands[i] = newValue;

                var use = _operandUses[i];
                oldValue.RemoveUse(use);
                newValue.AddUse(use);
            }
        }
    }
    /// <summary> Replaces the operand at `operIndex` with `newOper`. </summary>
    public void ReplaceOperand(int operIndex, Value newOper)
    {
        var prevOper = _operands[operIndex];
        if (newOper != prevOper) {
            _operands[operIndex] = newOper;

            var use = _operandUses[operIndex];
            prevOper?.RemoveUse(use);
            newOper.AddUse(use);
        }
    }

    /// <summary> 
    /// Extends the operand array by `amount` and returns the index of the first new element. 
    /// Newly allocated elements are set to null, they should be initialized immediately after calling this,
    /// using ReplaceOperand(). 
    /// </summary>
    protected int GrowOperands(int amount)
    {
        int oldLen = _operands.Length;
        Array.Resize(ref _operands, oldLen + amount);
        Array.Resize(ref _operandUses, oldLen + amount);
        for (int i = oldLen; i < oldLen + amount; i++) {
            _operandUses[i] = new Use() { User = this, OperIdx = i };
        }
        return oldLen;
    }
    /// <summary> Removes operands in the specified range. </summary>
    protected void RemoveOperands(int startIndex, int count)
    {
        Assert(startIndex >= 0 && startIndex + count <= _operands.Length);

        var newOpers = new Value[_operands.Length - count];
        var newUses = new Use[newOpers.Length];
        //Copy prefix
        for (int i = 0; i < startIndex; i++) {
            newOpers[i] = _operands[i];
            newUses[i] = _operandUses[i];
        }
        //Drop middle
        for (int i = startIndex; i < startIndex + count; i++) {
            _operands[i].RemoveUse(_operandUses[i]);
        }
        //Shift postfix
        for (int i = startIndex + count; i < _operands.Length; i++) {
            int j = i - count;
            newOpers[j] = _operands[i];
            newUses[j] = _operandUses[i];
            newUses[j].OperIdx = j;
        }
        _operands = newOpers;
        _operandUses = newUses;
    }

    public abstract void Accept(InstVisitor visitor);

    public override void PrintAsOperand(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append(slotTracker.GetName(this));
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
            ResultType.Print(sb, slotTracker, false);
            sb.Append(" ");
            PrintAsOperand(sb, slotTracker);
            sb.Append(" = ");
        }
        sb.Append(InstName);
    }
    /// <summary> Prints the instruction operands. </summary>
    protected virtual void PrintOperands(StringBuilder sb, SlotTracker slotTracker)
    {
        for (int i = 0; i < _operands.Length; i++) {
            sb.Append(i == 0 ? " " : ", ");
            _operands[i].PrintAsOperand(sb, slotTracker);
        }
    }
    protected override SlotTracker GetDefaultSlotTracker()
    {
        return Block?.Method.GetSlotTracker() ?? base.GetDefaultSlotTracker();
    }
}