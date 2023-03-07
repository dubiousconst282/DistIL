namespace DistIL.IR;

public class BasicBlock : TrackedValue
{
    public MethodBody Method { get; internal set; }

    public PredIterator Preds => new(this);
    public SuccIterator Succs => new(this);

    public Instruction First { get; private set; } = null!;
    /// <remarks> Must be one of: 
    /// <see cref="ReturnInst"/>, <see cref="BranchInst"/>, <see cref="SwitchInst"/>,
    /// <see cref="ThrowInst"/>, <see cref="LeaveInst"/>, or <see cref="ResumeInst"/>. </remarks>
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
                //Guard has exactly one or two blocks: [handlerBlock, filterBlock?]
                count += inst.Operands.Length;
            }
            if (IsBranchWithSuccEdges(Last)) {
                //Unconditional branches only have one operand, cond and switches have at least 2.
                //See comment in SuccIterator for details.
                int numOpers = Last.Operands.Length;
                count += numOpers - (numOpers >= 2 ? 1 : 0);
            }
            return count;
        }
    }
    /// <summary> Returns the first instruction that is not a <see cref="GuardInst"/>. </summary>
    public Instruction FirstNonGuard {
        get {
            var inst = First;
            while (inst is GuardInst) {
                inst = inst.Next!;
            }
            return inst;
        }
    }
    /// <summary> Returns the first instruction that is not a <see cref="PhiInst"/> or <see cref="GuardInst"/>. </summary>
    public Instruction FirstNonHeader {
        get {
            var inst = First;
            while (inst is PhiInst or GuardInst) {
                inst = inst.Next!;
            }
            return inst;
        }
    }

    /// <summary> Whether the block starts with a <see cref="PhiInst"/> or <see cref="GuardInst"/>. </summary>
    public bool HasHeader => First != null && First.IsHeader;

    internal BasicBlock(MethodBody method)
    {
        Method = method;
    }

    /// <summary> Inserts <paramref name="newInst"/> before the first instruction in this block. </summary>
    public void InsertFirst(Instruction newInst) => InsertRange(null, newInst, newInst);

    /// <summary> Inserts <paramref name="newInst"/> after the last instruction in this block. </summary>
    public void InsertLast(Instruction newInst) => InsertRange(Last, newInst, newInst);

    /// <summary> Inserts <paramref name="newInst"/> before <paramref name="inst"/>. If <paramref name="inst"/> is null, <paramref name="newInst"/> will be inserted at the block start. </summary>
    public void InsertBefore(Instruction? inst, Instruction newInst) => InsertRange(inst?.Prev, newInst, newInst);

    /// <summary> Inserts <paramref name="newInst"/> after <paramref name="inst"/>. If <paramref name="inst"/> is null, <paramref name="newInst"/> will be inserted at the block start. </summary>
    public void InsertAfter(Instruction? inst, Instruction newInst) => InsertRange(inst, newInst, newInst);

    /// <summary> Inserts <paramref name="newInst"/> before the block terminator, if one exists. </summary>
    public void InsertAnteLast(Instruction newInst)
        => InsertRange(Last is { IsBranch: true } ? Last.Prev : Last, newInst, newInst);

    //Inserts a range of instructions into this block after `pos` (null means before the first instruction).
    internal void InsertRange(Instruction? pos, Instruction rangeFirst, Instruction rangeLast, bool transfering = false)
    {
        //Set parent block for range
        for (var inst = rangeFirst; true; inst = inst.Next!) {
            Ensure.That(inst.Block == null || transfering);
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

    /// <summary> Moves a range of instructions from this block to <paramref name="newParent"/>, after <paramref name="newParentPos"/> (null means before the first instruction in <paramref name="newParent"/>). </summary>
    public void MoveRange(BasicBlock newParent, Instruction? newParentPos, Instruction first, Instruction last)
    {
        Ensure.That(newParentPos == null || newParentPos?.Block == newParent);
        Ensure.That(first.Block == this && last.Block == this);

        UnlinkRange(first, last);
        newParent.InsertRange(newParentPos, first, last, transfering: true);
    }

    /// <summary> Detaches <paramref name="inst"/> from this block. </summary>
    internal void Remove(Instruction inst)
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

    public PhiInst InsertPhi(PhiInst phi)
    {
        if (FirstNonHeader is { } pos) { //ensure insertion order is respected
            InsertBefore(pos, phi);
        } else {
            InsertLast(phi);
        }
        return phi;
    }
    public PhiInst InsertPhi(TypeDesc resultType) => InsertPhi(new PhiInst(resultType));

    /// <summary>
    /// Splits this block, moving instructions starting from <paramref name="pos"/> (inclusive) to the new block,
    /// and adds a unconditional branch to the new block (or <paramref name="branchTo"/> if specified).
    /// </summary>
    /// <remarks> Note that <paramref name="pos"/> cannot be a PhiInst/GuardInst and it must be in this block. </remarks>
    public BasicBlock Split(Instruction pos, BasicBlock? branchTo = null)
    {
        Ensure.That(pos.Block == this && !pos.IsHeader);

        var newBlock = Method.CreateBlock(insertAfter: this);
        RedirectSuccPhis(newBlock);
        MoveRange(newBlock, null, pos, Last);
        SetBranch(branchTo ?? newBlock);
        return newBlock;
    }

    /// <summary> 
    /// Inserts an intermediate block between the edge (<see langword="this"/> -> <paramref name="succ"/>), if it is critical.
    /// Returns the new intermediate block, or this if the edge is not critical.
    /// </summary>
    public BasicBlock SplitCriticalEdge(BasicBlock succ)
    {
        if (NumSuccs < 2 || succ.NumPreds < 2) {
            return this;
        }
        var intermBlock = Method.CreateBlock(insertAfter: succ.Prev).SetName("CritEdge");
        intermBlock.SetBranch(succ);

        //Redirect branches/phis to the intermediate block
        Last.ReplaceOperand(succ, intermBlock);
        succ.RedirectPhis(this, intermBlock);

        return intermBlock;
    }

    /// <summary> Replaces the incomming block of all phis in successor blocks from this block to <paramref name="newPred"/>. </summary>
    public void RedirectSuccPhis(BasicBlock? newPred, bool removeTrivialPhis = true)
    {
        foreach (var succ in Succs) {
            succ.RedirectPhis(this, newPred, removeTrivialPhis);
        }
    }

    /// <summary>
    /// Replaces the incomming block of all phis in this block from <paramref name="oldPred"/> to <paramref name="newPred"/>. 
    /// If <paramref name="newPred"/> is null, <paramref name="oldPred"/> is removed from the phi arguments.
    /// </summary>
    /// <param name="removeTrivialPhis">Remove phis with a single argument.</param>
    public void RedirectPhis(BasicBlock oldPred, BasicBlock? newPred, bool removeTrivialPhis = true)
    {
        foreach (var phi in Phis()) {
            if (newPred != null) {
                phi.ReplaceOperand(oldPred, newPred);
            } else {
                phi.RemoveArg(oldPred, removeTrivialPhis);
            }
        }
    }

    /// <summary> Replaces the block terminator with <paramref name="newBranch"/>. </summary>
    public void SetBranch(Instruction newBranch)
    {
        Ensure.That(newBranch.IsBranch);

        if (Last != null && Last.IsBranch) {
            Last.Remove();
        }
        InsertLast(newBranch);
    }
    /// <summary> Replaces the block terminator with a unconditional branch to <paramref name="target"/>. </summary>
    public void SetBranch(BasicBlock target)
    {
        SetBranch(new BranchInst(target));
    }

    /// <summary> Removes this block from the parent method, and clear uses from child instruction operands. </summary>
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
        for (var inst = First; inst != null; inst = inst.Next) {
            yield return inst;
        }
    }
    public IEnumerable<Instruction> Reversed()
    {
        for (var inst = Last; inst != null; inst = inst.Prev) {
            yield return inst;
        }
    }

    public IEnumerable<PhiInst> Phis()
    {
        for (var inst = FirstNonGuard; inst is PhiInst phi; inst = inst.Next) {
            yield return phi;
        }
    }
    /// <summary> Enumerates all <see cref="GuardInst"/>s in this block. </summary>
    /// <remarks> Blocks can have both guards and phis, but guards must always come first. </remarks>
    public IEnumerable<GuardInst> Guards()
    {
        for (var inst = First; inst is GuardInst guard; inst = inst.Next) {
            yield return guard;
        }
    }
    public IEnumerable<Instruction> NonPhis()
    {
        for (var inst = FirstNonHeader; inst != null; inst = inst.Next) {
            yield return inst;
        }
    }

    private static bool IsBranchWithSuccEdges(Instruction? inst)
        => inst is BranchInst or SwitchInst or LeaveInst;

    //Enumerating block users (ignoring phis) will lead directly to predecessors.
    //GuardInst`s will not yield duplicates because handler blocks can only have one predecessor guard.
    //SwitchInst has a special representation to avoid duplicated block use edges.
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

        public override string ToString() => "[" + string.Join(", ", this.AsEnumerable()) + "]";
    }
    //Enumerating guard and branch instruction operands will directly lead to successors.
    public struct SuccIterator : Iterator<BasicBlock>
    {
        Instruction? _currInst;
        int _operIdx;

        public BasicBlock Current { get; private set; } = null!;

        internal SuccIterator(BasicBlock block)
        {
            _currInst = block.Last;

            if (IsBranchWithSuccEdges(_currInst)) {
                //Unconditional branches only have one operand, cond and switches have at least 2.
                //  Branch: [thenBlock]
                //  CondBr: [cond, thenBlock, elseBlock]
                //  Switch: [value, targetBlock0, targetBlock1, ...]  (targets are never duplicated)
                //  Guard:  [handlerBlock, filterBlock?]
                //  Leave:  [targetBlock]
                var opers = _currInst.Operands;
                _operIdx = opers.Length >= 2 && _currInst is not GuardInst ? 1 : 0;
            } else {
                _currInst = block.First as GuardInst;
                Ensure.That(block.Last is not GuardInst); //prevents an infinite loop in MoveNext()
            }
        }

        public bool MoveNext()
        {
            Debug.Assert(IsBranchWithSuccEdges(_currInst) || _currInst is GuardInst or null);

            while (_currInst != null) {
                var opers = _currInst.Operands;
                if (_operIdx < opers.Length) {
                    Current = (BasicBlock)opers[_operIdx++];
                    return true;
                }
                //If `_currInst` is the terminator (next == null), go back and start looking at guards
                _currInst = (_currInst.Next ?? _currInst.Block.First) as GuardInst;
                _operIdx = 0;
            }
            return false;
        }

        public override string ToString() => "[" + string.Join(", ", this.AsEnumerable()) + "]";
    }
}