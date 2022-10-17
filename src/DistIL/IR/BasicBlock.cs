namespace DistIL.IR;

public class BasicBlock : TrackedValue
{
    public MethodBody Method { get; internal set; }

    /// <remarks> Note: <see cref="SwitchInst"/> may cause the same block to be yielded more than once. </remarks>
    public PredIterator Preds => new(this);
    /// <remarks> Note: <see cref="SwitchInst"/> may cause the same block to be yielded more than once. </remarks>
    public SuccIterator Succs => new(this);

    public Instruction First { get; private set; } = null!;
    /// <remarks> May be one of: 
    /// <see cref="ReturnInst"/>, <see cref="BranchInst"/>, <see cref="SwitchInst"/>,
    /// <see cref="ThrowInst"/>, <see cref="LeaveInst"/>, or <see cref="ContinueInst"/>. </remarks>
    public Instruction Last { get; private set; } = null!;

    public BasicBlock? Prev { get; set; }
    public BasicBlock? Next { get; set; }

    public int NumPreds {
        get => Users().Count(u => u is not PhiInst);
    }
    public int NumSuccs {
        get {
            int count = 0;
            for (var inst = First; inst is GuardInst; inst = inst.Next) {
                count++;
            }
            if (IsBranchLike(Last)) {
                //Uncond branches only have one operand, cond and switches have at least 2.
                int numOpers = Last.Operands.Length;
                count += numOpers - (numOpers >= 2 ? 1 : 0);
            }
            return count;
        }
    }
    public Instruction FirstNonPhi {
        get {
            var inst = First;
            while (inst is PhiInst) {
                inst = inst.Next!;
            }
            return inst;
        }
    }

    /// <summary> Whether the block starts with a <see cref="PhiInst"/> or <see cref-"GuardInst"/>. </summary>
    public bool HasHeader => First != null && First.IsHeader;

    internal BasicBlock(MethodBody method)
    {
        Method = method;
    }

    /// <summary> Inserts `newInst` before the first instruction in this block. </summary>
    public void InsertFirst(Instruction newInst) => InsertRange(null, newInst, newInst);
    /// <summary> Inserts `newInst` after the last instruction in this block. </summary>
    public void InsertLast(Instruction newInst) => InsertRange(Last, newInst, newInst);
    /// <summary> Inserts `newInst` before `inst`. If `inst` is null, `newInst` will be inserted at the block start. </summary>
    public void InsertBefore(Instruction? inst, Instruction newInst) => InsertRange(inst?.Prev, newInst, newInst);
    /// <summary> Inserts `newInst` after `inst`. If `inst` is null, `newInst` will be inserted at the block start. </summary>
    public void InsertAfter(Instruction? inst, Instruction newInst) => InsertRange(inst, newInst, newInst);

    /// <summary> Inserts a range of instructions into this block after pos (null means before the first instruction). </summary>
    /// <param name="rangeFirst">The first instruction in the range.</param>
    /// <param name="rangeLast">The last instruction in the range, or `first` if only one instruction is to be added.</param>
    public void InsertRange(Instruction? pos, Instruction rangeFirst, Instruction rangeLast)
    {
        //Set parent block for range
        for (var inst = rangeFirst; true; inst = inst.Next!) {
            Ensure.That(inst.Block != this); //prevent creating cycles
            inst.Block = this;
            if (inst == rangeLast) break;
        }

        if (pos != null) {
            rangeFirst.Prev = pos;
            rangeLast.Next = pos.Next;

            if (pos.Next != null) {
                pos.Next.Prev = rangeLast;
            } else {
                Debug.Assert(pos == Last);
                Last = rangeLast;
            }
            pos.Next = rangeFirst;
        } else {
            if (First != null) {
                First.Prev = rangeLast;
            }
            rangeLast.Next = First;
            rangeFirst.Prev = null;
            First = rangeFirst;
            Last ??= rangeLast;
        }
    }

    /// <summary> Moves a range of instructions from this block to `newParent`, after `newParentPos` (null means before the first instruction in `newParent`). </summary>
    public void MoveRange(BasicBlock newParent, Instruction? newParentPos, Instruction first, Instruction last)
    {
        Ensure.That(newParentPos == null || newParentPos?.Block == newParent);
        Ensure.That(first.Block == this && last.Block == this);

        UnlinkRange(first, last);
        newParent.InsertRange(newParentPos, first, last);
    }

    public void Remove(Instruction inst)
    {
        Ensure.That(inst.Block == this);
        inst.Block = null!; //prevent inst from being removed again

        UnlinkRange(inst, inst);
    }

    private void UnlinkRange(Instruction rangeFirst, Instruction rangeLast)
    {
        if (rangeFirst.Prev != null) {
            rangeFirst.Prev.Next = rangeLast.Next;
        } else {
            First = rangeLast.Next!;
        }
        if (rangeLast.Next != null) {
            rangeLast.Next.Prev = rangeFirst.Prev;
        } else {
            Last = rangeFirst.Prev!;
        }
    }

    public PhiInst AddPhi(PhiInst phi)
    {
        if (First != null) {
            InsertBefore(FirstNonPhi, phi);
        } else {
            InsertFirst(phi);
        }
        return phi;
    }
    public PhiInst AddPhi(TypeDesc resultType) => AddPhi(new PhiInst(resultType));

    /// <summary>
    /// Splits this block, moving instructions starting from `pos` to the new block,
    /// and adds a unconditional branch to the new block.
    /// Note that `pos` cannot be a PhiInst/GuardInst and it must be in this block.
    /// </summary>
    public BasicBlock Split(Instruction pos)
    {
        Ensure.That(pos.Block == this && !pos.IsHeader);

        var newBlock = Method.CreateBlock();
        MoveRange(newBlock, null, pos, Last);
        //Add branch to new block
        SetBranch(newBlock);
        return newBlock;
    }

    /// <summary> Insert intermediate blocks between critical predecessor edges. </summary>
    public void SplitCriticalEdges()
    {
        if (NumPreds < 2) return;

        foreach (var pred in Preds) {
            if (pred.NumSuccs < 2) continue;

            var intermBlock = Method.CreateBlock(insertAfter: pred).SetName("CritEdge");
            intermBlock.SetBranch(this);

            //Redirect branches/phis to the intermediate block
            pred.Last.ReplaceOperands(this, intermBlock);
            foreach (var phi in Phis()) {
                phi.ReplaceOperands(pred, intermBlock);
            }
        }
    }

    /// <summary> Replaces the incomming block of all phis in successor blocks from _this block_ with `newPred`. </summary>
    public void RedirectSuccPhis(BasicBlock newPred)
    {
        foreach (var succ in Succs) {
            foreach (var phi in succ.Phis()) {
                phi.ReplaceOperands(this, newPred);
            }
        }
    }

    /// <summary> Replaces the block terminator with `newBranch`. </summary>
    public void SetBranch(Instruction newBranch)
    {
        Ensure.That(newBranch.IsBranch);

        if (Last != null && Last.IsBranch) {
            Last.Remove();
        }
        InsertLast(newBranch);
    }
    /// <summary> Replaces the block terminator with a unconditional branch to `target`. </summary>
    public void SetBranch(BasicBlock target)
    {
        SetBranch(new BranchInst(target));
    }

    /// <summary> 
    /// Removes this block from the parent method, clear edges, and uses from child instruction operands.
    /// </summary>
    public void Remove()
    {
        foreach (var inst in this) {
            inst.RemoveOperandUses();
        }
        Method.RemoveBlock(this);
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print(ctx.SymTable.GetName(this));
    }
    public override SymbolTable? GetSymbolTable()
    {
        return Method?.GetSymbolTable();
    }

    public IEnumerator<Instruction> GetEnumerator()
    {
        if (First == null) yield break;

        var inst = First;
        var last = Last; //copy to allow for removes while iterating
        while (true) {
            yield return inst;
            if (inst == last) break;
            inst = inst.Next!;
        }
    }
    public IEnumerable<Instruction> Reversed()
    {
        var inst = Last;
        var first = First; //copy to allow for removes while iterating
        while (true) {
            yield return inst;
            if (inst == first) break;
            inst = inst.Prev!;
        }
    }

    public IEnumerable<PhiInst> Phis()
    {
        var inst = First;
        while (inst is PhiInst phi) {
            yield return phi;
            inst = inst.Next!;
        }
    }
    /// <summary> Enumerates all <see cref="GuardInst"/> in this block. </summary>
    /// <remarks> Blocks with guards (entry of a region) should not have phi instructions. </remarks>
    public IEnumerable<GuardInst> Guards()
    {
        var inst = First;
        while (inst is GuardInst guard) {
            yield return guard;
            inst = inst.Next!;
        }
    }
    public IEnumerable<Instruction> NonPhis()
    {
        var inst = FirstNonPhi;
        if (inst == null) yield break;
        var last = Last; //copy to allow for removes while iterating

        while (true) {
            yield return inst;
            if (inst == last) break;
            inst = inst.Next!;
        }
    }

    private static bool IsBranchLike(Instruction? inst)
        => inst is BranchInst or SwitchInst or LeaveInst;

    //Enumerating block users (ignoring phis) will lead directly to predecessors.
    //GuardInst`s will not yield duplicates because handler blocks can only have one predecessor guard;
    //this is not the case for SwitchInst, since there might be duplicates and Users() don't guarantee uniqueness.
    public struct PredIterator : Iterator<BasicBlock>
    {
        ValueUserIterator _users;

        public BasicBlock Current => _users.Current.Block;

        internal PredIterator(BasicBlock block)
            => _users = block.Users();

        public bool MoveNext()
        {
            while (_users.MoveNext()) {
                if (_users.Current is not PhiInst) {
                    return true;
                }
            }
            return false;
        }
    }
    //Enumerating guard and branch instruction operands will directly lead to successors.
    public struct SuccIterator : Iterator<BasicBlock>
    {
        BasicBlock _block;
        Instruction? _currInst;
        int _operIdx;

        public BasicBlock Current { get; private set; } = null!;

        internal SuccIterator(BasicBlock block)
        {
            _block = block;
            _currInst = block.Last;

            if (!IsBranchLike(_currInst)) {
                _currInst = _block.First as GuardInst;
            }
            Debug.Assert(_block.Last is not GuardInst);
        }

        public bool MoveNext()
        {
            Debug.Assert(IsBranchLike(_currInst) || _currInst is GuardInst or null);

            while (true) {
                if (_currInst == null) {
                    return false;
                }
                //Uncond branches only have one operand, cond and switches have at least 2.
                //  Branch: [thenBlock]
                //  CondBr: [cond, thenBlock, elseBlock]
                //  Switch: [value, defaultBlock, case0?, case1?, ...]
                //  Guard:  [handlerBlock, filterBlock?]
                //  Leave:  [targetBlock]
                var opers = _currInst.Operands;
                int offset = opers.Length >= 2 && _currInst is not GuardInst ? 1 : 0;

                if (_operIdx + offset < opers.Length) {
                    Current = (BasicBlock)opers[_operIdx + offset];
                    _operIdx++;
                    return true;
                }
                var nextInst = 
                    _currInst.Next == null && _currInst != _block.First
                        ? _block.First //(if `_currInst` is the terminator)
                        : _currInst.Next;
                _currInst = nextInst as GuardInst;
                _operIdx = 0;
            }
        }
    }
}