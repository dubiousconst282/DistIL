namespace DistIL.IR;

//TODO: Encapsulate into InstList and BlockList to avoid manual manipulation of nodes/links
public class BasicBlock : Value
{
    public Method Method { get; internal set; }

    public List<BasicBlock> Preds { get; } = new();
    public List<BasicBlock> Succs { get; } = new();

    public Instruction First { get; set; } = null!;
    public Instruction Last { get; set; } = null!; //Either a BranchInst or ReturnInst

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

    /// <summary> Whether <see cref="Instruction.Order" /> values are valid. </summary>
    public bool OrderValid { get; private set; }

    internal BasicBlock(Method method)
    {
        Method = method;
    }

    /// <summary> Adds a successor to this block. </summary>
    public void Connect(BasicBlock succ)
    {
        //Allow calls with duplicated edges, but don't dupe in the list (SwitchInst)
        if (!Succs.Contains(succ)) {
            Succs.Add(succ);
        }
        Ensure(!succ.Preds.Contains(this));
        succ.Preds.Add(this);
    }

    /// <summary> Removes a successor from this block. </summary>
    public void Disconnect(BasicBlock succ)
    {
        Succs.Remove(succ);
        succ.Preds.Remove(this);
    }

    /// <summary> Inserts `newInst` before the first instruction in this block. </summary>
    public void InsertFirst(Instruction newInst)
    {
        Assert(newInst.Prev == null && newInst.Next == null);

        if (First == null) {
            First = Last = newInst;
            newInst.Block = this;
            OnCodeChanged();
        } else {
            InsertBefore(First, newInst);
        }
    }
    /// <summary> Inserts `newInst` after the last instruction in this block. </summary>
    public void InsertLast(Instruction newInst)
    {
        Assert(newInst.Prev == null && newInst.Next == null);

        if (Last == null) {
            First = Last = newInst;
            newInst.Block = this;
            OnCodeChanged();
        } else {
            InsertAfter(Last, newInst);
        }
    }
    /// <summary> Inserts `newInst` before `inst`. </summary>
    public void InsertBefore(Instruction inst, Instruction newInst)
    {
        newInst.Block = this;
        newInst.Prev = inst.Prev;
        newInst.Next = inst;

        if (inst.Prev != null) {
            inst.Prev.Next = newInst;
        } else {
            First = newInst;
        }
        inst.Prev = newInst;
        OnCodeChanged();
    }
    /// <summary> Inserts `newInst` after `inst`. </summary>
    public void InsertAfter(Instruction inst, Instruction newInst)
    {
        newInst.Block = this;
        newInst.Prev = inst;
        newInst.Next = inst.Next;

        if (inst.Next != null) {
            inst.Next.Prev = newInst;
        } else {
            Last = newInst;
        }
        inst.Next = newInst;
        OnCodeChanged();
    }
    
    public void Remove(Instruction inst, bool removeOperUses = true)
    {
        Ensure(inst.Block == this);
        
        if (inst.Prev != null) {
            inst.Prev.Next = inst.Next;
        } else {
            First = inst.Next!;
        }

        if (inst.Next != null) {
            inst.Next.Prev = inst.Prev;
        } else {
            Last = inst.Prev!;
        }

        inst.Block = null!; //to ensure it can't be removed again
        OnCodeChanged();
    }

    private void OnCodeChanged()
    {
        OrderValid = false;
        Method.InvalidateSlots();
    }

    /// <summary> Update <see cref="Instruction.Order"/> values if needed. </summary>
    public void EnsureOrder()
    {
        if (OrderValid) return;
        OrderValid = true;
        
        int i = 0;
        foreach (var inst in this) {
            inst.Order = i++;
        }
    }

    public PhiInst AddPhi(RType resultType)
    {
        var phi = new PhiInst(resultType);
        InsertBefore(FirstNonPhi, phi);
        return phi;
    }
    public PhiInst AddPhi(params PhiArg[] args)
    {
        var phi = new PhiInst(args);
        InsertBefore(FirstNonPhi, phi);
        return phi;
    }
    public PhiInst AddPhi(IEnumerable<PhiArg> args)
    {
        var phi = new PhiInst(args.ToArray());
        InsertBefore(FirstNonPhi, phi);
        return phi;
    }

    /// <summary>
    /// Splits this block before the specified instruction.
    /// This methods adds an unconditional branch to the new block before `pos`,
    /// and moves instructions after it to the new block.
    /// Note that `pos` cannot be a PhiInst and it must be in this block.
    /// </summary>
    public BasicBlock Split(Instruction pos)
    {
        Ensure(pos.Block == this && pos is not PhiInst);

        var newBlock = Method.CreateBlock();
        var branchToNewBlock = new BranchInst(newBlock);
        InsertBefore(pos, branchToNewBlock);

        //Unlink previous instructions
        if (pos.Prev != null) {
            pos.Prev.Next = null;
            pos.Prev = null;
        }
        //Move remaining instructions to the new block
        newBlock.First = pos;
        newBlock.Last = Last;
        Last = branchToNewBlock;

        foreach (var inst in newBlock) {
            inst.Block = newBlock;
        }
        //Update edges
        foreach (var succ in Succs) {
            succ.Preds.Remove(this);
            newBlock.Connect(succ);
        }
        Succs.Clear();
        Connect(newBlock);

        return newBlock;
    }

    /// <summary> 
    /// Replaces the last instruction in the block with the specified branch and
    /// update successors accordingly. 
    /// </summary>
    public void SetBranch(BranchInst br)
    {
        Last?.Remove();
        InsertLast(br);

        //Disconnect successors
        foreach (var succ in Succs) {
            succ.Preds.Remove(this);
        }
        Succs.Clear();
        //Connect new branch targets
        Connect(br.Then);
        if (br.IsConditional) {
            Connect(br.Else);
        }
    }
    /// <summary> 
    /// Replaces the last instruction in the block with a unconditional branch to `target`
    /// and update successors accordingly. 
    /// </summary>
    public void SetBranch(BasicBlock target)
    {
        SetBranch(new BranchInst(target));
    }

    /// <summary> 
    /// Removes this block from the parent method, and clear its edges. 
    /// Will throw if the use list is not empty.
    /// </summary>
    public void Remove()
    {
        Ensure(Uses.Count == 0);

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

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append($"BB_{slotTracker.GetId(this):00}");
    }
    protected override SlotTracker GetDefaultSlotTracker()
    {
        return Method.GetSlotTracker();
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