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
    /// <summary> Whether this instruction may write to a memory location (variables, arrays, fields, pointers). </summary>
    public virtual bool MayWriteToMemory => false;

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
        if (_useDefs == null) return;

        for (int i = 0; i < _operands.Length; i++) {
            _operands[i].RemoveUse(ref _useDefs[i]);
        }
        _useDefs = null!;
    }

    /// <summary> Replaces operands set to `oldValue` with `oldValue`. </summary>
    public void ReplaceOperands(Value oldValue, Value newValue)
    {
        for (int i = 0; i < _operands.Length; i++) {
            if (_operands[i] == oldValue) {
                _operands[i] = newValue;

                oldValue.RemoveUse(ref _useDefs[i]);
                newValue.AddUse(this, i);
            }
        }
    }
    /// <summary> Replaces the operand at `operIndex` with `newValue`. </summary>
    public void ReplaceOperand(int operIndex, Value newValue)
    {
        var prevValue = _operands[operIndex];
        if (newValue != prevValue) {
            _operands[operIndex] = newValue;

            prevValue?.RemoveUse(ref _useDefs[operIndex]);
            newValue.AddUse(this, operIndex);
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
        Array.Resize(ref _useDefs, oldLen + amount);
        return oldLen;
    }
    /// <summary> Removes operands in the specified range. </summary>
    protected void RemoveOperands(int startIndex, int count)
    {
        Assert(startIndex >= 0 && startIndex + count <= _operands.Length);

        var oldOpers = _operands;
        var oldUses = _useDefs;

        //Remove uses from middle and postfix (we'll relocate it later)
        for (int i = startIndex; i < oldOpers.Length; i++) {
            oldOpers[i].RemoveUse(ref oldUses[i]);
        }

        var newOpers = _operands = new Value[oldOpers.Length - count];
        var newUses = _useDefs = new UseDef[newOpers.Length];

        //Copy prefix
        for (int i = 0; i < startIndex; i++) {
            newOpers[i] = oldOpers[i];
            newUses[i] = oldUses[i];
        }
        //Shift postfix
        for (int i = startIndex + count; i < oldOpers.Length; i++) {
            int j = i - count;
            newOpers[j] = oldOpers[i];
            oldOpers[i].AddUse(this, j); //Relocate use
        }
    }

    public abstract void Accept(InstVisitor visitor);

    public override void PrintAsOperand(PrintContext ctx)
    {
        ctx.Print(ctx.SymTable.GetName(this), PrintToner.VarName);
    }
    public override void Print(PrintContext ctx)
    {
        PrintPrefix(ctx);
        PrintOperands(ctx);
    }
    /// <summary> Prints the result variable and instruction name: [resultType operandName = ] instName </summary>
    protected virtual void PrintPrefix(PrintContext ctx)
    {
        if (HasResult) {
            ResultType.Print(ctx, includeNs: false);
            ctx.Print(" ");
            PrintAsOperand(ctx);
            ctx.Print(" = ");
        }
        ctx.Print(InstName, PrintToner.InstName);
    }
    /// <summary> Prints the instruction operands. </summary>
    protected virtual void PrintOperands(PrintContext ctx)
    {
        for (int i = 0; i < _operands.Length; i++) {
            ctx.Print(i == 0 ? " " : ", ");
            _operands[i].PrintAsOperand(ctx);
        }
    }
    protected override SymbolTable GetDefaultSymbolTable()
    {
        return Block?.Method.GetSymbolTable() ?? base.GetDefaultSymbolTable();
    }
}