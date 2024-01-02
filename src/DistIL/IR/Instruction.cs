namespace DistIL.IR;

public abstract class Instruction : TrackedValue
{
    public BasicBlock Block { get; set; } = null!;

    /// <summary> The previous instruction in the block. </summary>
    public Instruction? Prev { get; set; }
    /// <summary> The next instruction in the block. </summary>
    public Instruction? Next { get; set; }

    internal Value[] _operands;
    internal UseDef[] _useDefs;
    public ReadOnlySpan<Value> Operands => _operands;

    /// <summary> Location of the source CIL instruction. </summary>
    public SourceLocation Location { get; set; }

    public abstract string InstName { get; }

    /// <summary> Whether this instructions modifies global state, affects control flow, or throws exceptions. </summary>
    public virtual bool HasSideEffects => MayThrow || MayWriteToMemory || IsBranch;
    public virtual bool MayThrow => false;
    /// <summary> Whether this instruction is a branch, return, or affects control flow. </summary>
    public virtual bool IsBranch => false;
    /// <summary> Whether this instruction can be safely removed if it has no uses. </summary>
    public virtual bool SafeToRemove => !HasSideEffects;
    /// <summary> Whether this instruction may write to any memory location. </summary>
    public virtual bool MayWriteToMemory => false;
    /// <summary> Whether this instruction may read from a visible memory location (variable, array, field, pointer). </summary>
    public virtual bool MayReadFromMemory => false; // TODO: should this incur HasSideEffects?

    protected Instruction()
    {
        _operands = Array.Empty<Value>();
        _useDefs = Array.Empty<UseDef>();
    }
    protected Instruction(params Value[] opers)
    {
        _operands = opers;
        _useDefs = new UseDef[opers.Length];

        for (int i = 0; i < opers.Length; i++) {
            _operands[i].AddUse(this, i);
        }
    }

    /// <summary> Inserts this instruction before <paramref name="inst"/>. </summary>
    public void InsertBefore(Instruction inst) => inst.Block.InsertBefore(inst, this);

    /// <summary> Inserts this instruction after <paramref name="inst"/>. </summary>
    public void InsertAfter(Instruction inst) => inst.Block.InsertAfter(inst, this);

    /// <summary> Moves this instruction before <paramref name="inst"/>. </summary>
    public void MoveBefore(Instruction inst) => Block.MoveRange(inst.Block, inst.Prev, this, this);

    /// <summary> 
    /// Removes this instruction from the parent block and replaces its uses with the specified value. <br/>
    /// If <paramref name="newValue"/> is an instruction with no parent block and <c>insertIfInst == true</c>, it will be added 
    /// in the same place as this instruction.
    /// </summary>
    /// <remarks> 
    /// Once this method returns, this instruction should be considered invalid and must not be added in a block again.
    /// The <see cref="Operands"/> array is left unmodified, but uses are removed.
    /// </remarks>
    public void ReplaceWith(Value newValue, bool insertIfInst = false)
    {
        Debug.Assert(newValue != this);
        
        if (insertIfInst && newValue is Instruction newInst && newInst.Block == null) {
            Block.InsertAfter(this, newInst);
        }
        ReplaceUses(newValue);
        Remove();
    }

    /// <summary> Removes this instruction from the parent basic block. </summary>
    /// <remarks> 
    /// Once this method returns, this instruction should be considered invalid and should not be added in a block again.
    /// The <see cref="Operands"/> array is left unmodified, but uses are removed.
    /// </remarks>
    public void Remove()
    {
        Block?.Remove(this);
        RemoveOperandUses();
    }

    internal void RemoveOperandUses()
    {
        if (_useDefs == null) return;

        for (int i = 0; i < _operands.Length; i++) {
            _operands[i].RemoveUse(this, i);
        }
        _useDefs = null!;
    }

    /// <summary> Replaces all operands set to <paramref name="oldValue"/> with <paramref name="newValue"/>. </summary>
    public void ReplaceOperand(Value oldValue, Value newValue)
    {
        for (int i = 0; i < _operands.Length; i++) {
            if (_operands[i] == oldValue) {
                _operands[i] = newValue;

                oldValue.RemoveUse(this, i);
                newValue.AddUse(this, i);
            }
        }
    }
    /// <summary> Replaces the operand at <paramref name="operIndex"/> with <paramref name="newValue"/>. </summary>
    public void ReplaceOperand(int operIndex, Value newValue)
    {
        var prevValue = _operands[operIndex];
        if (newValue != prevValue) {
            _operands[operIndex] = newValue;

            prevValue?.RemoveUse(this, operIndex);
            newValue.AddUse(this, operIndex);
        }
    }

    /// <summary> Returns a reference wrapper to the operand at <paramref name="index"/>. </summary>
    public UseRef GetOperandRef(int index)
    {
        Ensure.IndexValid(index, _operands.Length);
        return new(this, index);
    }

    /// <summary> 
    /// Extends the operand array by <paramref name="amount"/> and returns the index of the first new element. <br/>
    /// Newly allocated elements are set to null, they should be initialized immediately using <see cref="ReplaceOperand(int, Value)"/>.
    /// </summary>
    protected int GrowOperands(int amount)
    {
        int oldLen = _operands.Length;
        Array.Resize(ref _operands, oldLen + amount);
        Array.Resize(ref _useDefs, oldLen + amount);
        return oldLen;
    }
    /// <summary> Removes operands in the specified range. </summary>
    protected void RemoveOperands(int startIndex, int count)
    {
        Debug.Assert(startIndex >= 0 && startIndex + count <= _operands.Length);

        var oldOpers = _operands;
        var oldUses = _useDefs;

        // Remove uses from middle and postfix, relocate them later
        for (int i = startIndex; i < oldOpers.Length; i++) {
            oldOpers[i].RemoveUse(this, i);
        }

        var newOpers = _operands = new Value[oldOpers.Length - count];
        var newUses = _useDefs = new UseDef[newOpers.Length];

        // Copy prefix
        for (int i = 0; i < startIndex; i++) {
            newOpers[i] = oldOpers[i];
            newUses[i] = oldUses[i];
        }
        // Shift postfix
        for (int i = startIndex + count; i < oldOpers.Length; i++) {
            int j = i - count;
            newOpers[j] = oldOpers[i];
            newOpers[j].AddUse(this, j); // Relocate use
        }
    }

    public abstract void Accept(InstVisitor visitor);

    public override void PrintAsOperand(PrintContext ctx)
    {
        ctx.Print(ctx.SymTable.GetName(this), PrintToner.VarName);
    }
    public override void Print(PrintContext ctx)
    {
        if (HasResult) {
            PrintAsOperand(ctx);
            ctx.Print(" = ");
            PrintWithoutPrefix(ctx);
            ctx.Print(" -> ");
            ctx.Print(ResultType);
        } else {
            PrintWithoutPrefix(ctx);
        }
    }
    internal void PrintWithoutPrefix(PrintContext ctx)
    {
        ctx.Print(InstName, PrintToner.InstName);
        PrintOperands(ctx);
    }
    protected virtual void PrintOperands(PrintContext ctx)
    {
        for (int i = 0; i < _operands.Length; i++) {
            ctx.Print(i == 0 ? " " : ", ");
            ctx.PrintAsOperand(_operands[i]);
        }
    }
    public override SymbolTable? GetSymbolTable()
    {
        return Block?.Method.GetSymbolTable();
    }
}