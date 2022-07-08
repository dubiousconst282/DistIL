namespace DistIL.IR;

public class BasicBlock : TrackedValue
{
    public MethodBody Method { get; internal set; }

    public List<BasicBlock> Preds { get; } = new();
    public List<BasicBlock> Succs { get; } = new();

    public Instruction First { get; private set; } = null!;
    public Instruction Last { get; private set; } = null!; //Either a BranchInst or ReturnInst

    public BasicBlock? Prev { get; set; }
    public BasicBlock? Next { get; set; }

    public Instruction FirstNonPhi {
        get {
            var inst = First;
            while (inst is PhiInst) {
                inst = inst.Next!;
            }
            return inst;
        }
    }

    /// <summary> Whether the block starts with a `PhiInst` or `GuardInst`. </summary>
    public bool HasHeader => First is PhiInst or GuardInst;

    internal BasicBlock(MethodBody method)
    {
        Method = method;
    }

    /// <summary> Adds a successor to this block. </summary>
    public void Connect(BasicBlock succ)
    {
        Ensure(!succ.Preds.Contains(this));
        //Allow calls with duplicated edges, but don't dupe in the list (SwitchInst)
        if (!Succs.Contains(succ)) {
            Succs.Add(succ);
        }
        succ.Preds.Add(this);
    }

    /// <summary> Removes a successor from this block. </summary>
    public void Disconnect(BasicBlock succ)
    {
        Succs.Remove(succ);
        succ.Preds.Remove(this);
    }

    public void Reconnect(BasicBlock prevSucc, BasicBlock newSucc)
    {
        Disconnect(prevSucc);
        Connect(newSucc);
    }

    /// <summary> Remove edges from the branch targets. </summary>
    /// <param name="redirectPhisTo"> If not null, incomming blocks for phis of branch successors will be replaced with this block; otherwise, the argument will be removed. </param>
    public void DisconnectBranch(Instruction branch, BasicBlock? redirectPhisTo = null)
    {
        foreach (var oper in branch.Operands) {
            if (oper is not BasicBlock succ) continue;

            Disconnect(succ);

            foreach (var phi in succ.Phis()) {
                if (redirectPhisTo != null) {
                    phi.ReplaceOperands(this, redirectPhisTo);
                } else {
                    phi.RemoveArg(this, true);
                }
            }
        }
    }
    /// <summary> Create edges to the branch targets. </summary>
    public void ConnectBranch(Instruction branch)
    {
        foreach (var oper in branch.Operands) {
            if (oper is not BasicBlock succ) continue;

            Connect(succ);
        }
    }

    /// <summary> Inserts `newInst` before the first instruction in this block. </summary>
    public void InsertFirst(Instruction newInst) => InsertRange(null, newInst, newInst);
    /// <summary> Inserts `newInst` after the last instruction in this block. </summary>
    public void InsertLast(Instruction newInst) => InsertRange(Last, newInst, newInst);
    /// <summary> Inserts `newInst` before `inst`. </summary>
    public void InsertBefore(Instruction inst, Instruction newInst) => InsertRange(inst.Prev, newInst, newInst);
    /// <summary> Inserts `newInst` after `inst`. If `inst` is null, `newInst` will be inserted at the first position. </summary>
    public void InsertAfter(Instruction? inst, Instruction newInst) => InsertRange(inst, newInst, newInst);

    /// <summary> Inserts a range of instructions into this block after pos (null means before the first instruction). </summary>
    /// <param name="rangeFirst">The first instruction in the range.</param>
    /// <param name="rangeLast">The last instruction in the range, or `first` if only one instruction is to be added.</param>
    public void InsertRange(Instruction? pos, Instruction rangeFirst, Instruction rangeLast)
    {
        //Set parent block for range
        for (var inst = rangeFirst; true; inst = inst.Next!) {
            Ensure(inst.Block != this); //prevent creating cycles
            inst.Block = this;
            if (inst == rangeLast) break;
        }

        if (pos != null) {
            rangeFirst.Prev = pos;
            rangeLast.Next = pos.Next;

            if (pos.Next != null) {
                pos.Next.Prev = rangeLast;
            } else {
                Assert(pos == Last);
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
        OnCodeChanged();
    }

    /// <summary> Moves a range of instructions from this block to `newParent`, after `newParentPos` (null means before the first instruction in `newParent`). </summary>
    public void MoveRange(BasicBlock newParent, Instruction? newParentPos, Instruction first, Instruction last)
    {
        Ensure(newParentPos == null || newParentPos?.Block == newParent);
        Ensure(first.Block == this && last.Block == this);

        UnlinkRange(first, last);
        newParent.InsertRange(newParentPos, first, last);
        OnCodeChanged();
    }

    public void Remove(Instruction inst)
    {
        Ensure(inst.Block == this);
        inst.Block = null!; //prevent inst from being removed again

        UnlinkRange(inst, inst);
        OnCodeChanged();
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

    private void OnCodeChanged()
    {
        Method.InvalidateSlots();
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
        Ensure(pos.Block == this && !pos.IsHeader);

        var newBlock = Method.CreateBlock();
        MoveRange(newBlock, null, pos, Last);

        //Move edges to new block
        foreach (var succ in Succs) {
            succ.Preds.Remove(this);
            newBlock.Connect(succ);
        }
        Succs.Clear();
        //Add branch to new block
        SetBranch(newBlock);
        return newBlock;
    }

    /// <summary> Insert intermediate blocks between critical predecessor edges. </summary>
    public void SplitCriticalEdges()
    {
        if (Preds.Count < 2) return;

        for (int i = 0; i < Preds.Count; i++) {
            var pred = Preds[i];
            if (pred.Succs.Count < 2) continue;

            //Create an intermediate block jumping to this
            //(can't use SetBranch() because we're looping through the edges)
            var intermBlock = Method.CreateBlock(insertAfter: pred).SetName("CritEdge");
            intermBlock.InsertLast(new BranchInst(this));
            //Create `interm<->block` edge
            intermBlock.Succs.Add(this);
            Preds[i] = intermBlock;
            //Create `pred<->interm` edge
            pred.Reconnect(this, intermBlock);

            //Redirect branches/phis to the intermediate block
            pred.Last.ReplaceOperands(this, intermBlock);
            foreach (var phi in Phis()) {
                phi.ReplaceOperands(pred, intermBlock);
            }
        }
    }

    /// <summary> 
    /// Removes the last branch instruction from the block (if it exists),
    /// then adds `newBranch` (assumming it is a Branch/Switch/Return), and update edges accordingly.
    /// </summary>
    public void SetBranch(Instruction newBranch)
    {
        Ensure(newBranch.IsBranch);

        if (Last != null && Last.IsBranch) {
            DisconnectBranch(Last);
            Last.Remove();
        }
        ConnectBranch(newBranch);
        InsertLast(newBranch);
    }
    /// <summary> 
    /// Replaces the last instruction in the block with a unconditional branch to `target`, 
    /// and update edges accordingly. 
    /// </summary>
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
        foreach (var succ in Succs) {
            succ.Preds.Remove(this);
        }
        foreach (var pred in Preds) {
            pred.Succs.Remove(this);
        }
        Succs.Clear();
        Preds.Clear();

        Method.RemoveBlock(this);
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print(ctx.SymTable.GetName(this));
    }
    protected override SymbolTable GetDefaultSymbolTable()
    {
        return Method.GetSymbolTable();
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
    public IEnumerable<GuardInst> Guards()
    {
        var inst = FirstNonPhi;
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
}