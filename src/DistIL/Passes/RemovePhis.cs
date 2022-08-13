namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR;

//This pass is based on "Revisiting Out-of-SSA Translation for Correctness, Code Quality, and Efficiency"
//by Boissinot et al. (https://hal.inria.fr/inria-00349925v1/document)
//
//The idea is to isolate phis (split live range of its dependencies) by inserting parallel copies 
//for each argument into their respective predecessor blocks, and for each phi result (to solve the lost copy/swap problem).
//Then, all non-interfering copies can be coalesced into the same "congruence class" (MergeList).
//
//The paper describes an efficient algorithm to check for interferences in two merge lists, with
//the notion of value interference (two variables with the same value don't interfere).
//It also describes an algorithm to convert parallel copies into a simple sequence of load/stores.

/// <summary> Replace all phi instructions with local variables. </summary>
public class RemovePhis : MethodPass
{
    MethodBody _method = null!;
    Dictionary<Instruction, MergeNode> _mergeNodes = new();
    Dictionary<BasicBlock, (List<IntrinsicInst> Phis, List<IntrinsicInst> Args)> _copies = new();

    DominatorTree _domTree = null!;
    LivenessAnalysis _liveness = null!;

    public override void Run(MethodTransformContext ctx)
    {
        _method = ctx.Method;

        if (!IsolatePhis()) return;

        _domTree = ctx.GetAnalysis<DominatorTree>();
        _liveness = ctx.GetAnalysis<LivenessAnalysis>();
        CoalescePhis();
        CoalesceCopies();
        ResolveSlots();

        //Cleanup
        _method = null!;
        _mergeNodes.Clear();
        _copies.Clear();
        _domTree = null!;
        _liveness = null!;
    }

    //Insert copies for each phi argument at the end of the predecessor block, 
    //and for the result after all other phis.
    private bool IsolatePhis()
    {
        bool hasPhis = false;

        foreach (var block in _method) {
            var phiCopyInsPos = default(Instruction)!;

            //Split critical edges associated with constants
            foreach (var phi in block.Phis()) {
                foreach (var (pred, val) in phi) {
                    if (val is not Instruction) {
                        pred.SplitCriticalEdges();
                    }
                }
                phiCopyInsPos = phi;
            }

            //Create copies
            foreach (var phi in block.Phis()) {
                //Create copies for each phi argument
                for (int i = 0; i < phi.NumArgs; i++) {
                    var (pred, val) = phi.GetArg(i);
                    var copy = CreateCopy(val, pred.Last, true);
                    phi.SetValue(i, copy);
                }
                //Create copy for the phi and replace uses with it
                var resultCopy = CreateCopy(phi, phiCopyInsPos, false);
                phiCopyInsPos = resultCopy;

                phi.ReplaceUses(resultCopy);
                resultCopy.ReplaceOperand(0, phi); //ReplaceUses() will also replace the copy operand
            }
            hasPhis |= phiCopyInsPos != null;
        }

        return hasPhis;

        IntrinsicInst CreateCopy(Value source, Instruction pos, bool atEnd)
        {
            var copy = new IntrinsicInst(IntrinsicId.CopyDef, source.ResultType, source);
            if (atEnd) {
                copy.InsertBefore(pos);
            } else {
                copy.InsertAfter(pos);
            }
            ref var lists = ref _copies.GetOrAddRef(pos.Block);
            var list = (atEnd ? ref lists.Args : ref lists.Phis) ??= new();
            list.Add(copy);
            return copy;
        }
    }

    //Coalesce all phis and their arguments
    private void CoalescePhis()
    {
        foreach (var block in _method) {
            foreach (var phi in block.Phis()) {
                var list = new MergeList();
                AddMergeNode(list, phi);
                
                foreach (var (_, arg) in phi) {
                    if (arg is Instruction argI) {
                        AddMergeNode(list, argI);
                    }
                }
            }
        }
    }
    
    //Coalesce all non intersecting copies
    private void CoalesceCopies()
    {
        foreach (var (argCopies, phiCopies) in _copies.Values) {
            Coalesce(phiCopies);
            Coalesce(argCopies);
        }
        void Coalesce(List<IntrinsicInst>? copies)
        {
            if (copies == null) return;

            foreach (var copy in copies) {
                if (copy.Args[0] is not Instruction src) continue;

                var dstList = GetMergeList(copy);
                var srcList = GetMergeList(src);

                if (dstList != srcList && !Intersect(dstList, srcList)) {
                    Assert(!SlowCorrectIntersect(dstList, srcList));
                    MergeLists(dstList, srcList);

                    //Update nearest ancestor to "max(in, out)" based on the pre-dfs index
                    //(Is this all the unexplained "pre computation" in the paper?)
                    for (var node = dstList.First; node != null; node = node.Next) {
                        var ancIn = node.EqualAncestorIn;
                        var ancOut = node.EqualAncestorOut;
                        if (ancIn == null || (ancOut != null && ComesBefore(ancIn.Def, ancOut.Def))) {
                            node.EqualAncestorIn = ancOut;
                        }
                    }
                }
            }
        }
    }

    //Copy registers into slots of their corresponding merge lists, and sequentialize copies
    private void ResolveSlots()
    {
        var copiesToSeq = new List<(Variable Dst, Variable Src)>(); //temp list of vars that need to be sequentialized

        foreach (var (block, (phiCopies, argCopies)) in _copies) {
            //Note: blocks with no copies have no phis, so we don't need a separate loop over each block
            foreach (var phi in block.Phis()) {
                phi.Remove();
            }
            Resolve(block, phiCopies, false);
            Resolve(block, argCopies, true);
        }

        void Resolve(BasicBlock block, List<IntrinsicInst>? copies, bool atEnd)
        {
            if (copies == null) return;

            foreach (var copy in copies) {
                var dstSlot = GetMergeList(copy).GetSlot();
                var src = copy.Args[0];

                if (src is Instruction srcI) {
                    var srcSlot = GetMergeList(srcI).GetSlot();
                    //Copy source to its slot, and replace other uses with it
                    if (srcI.Block != null && srcI is not IntrinsicInst { Id: IntrinsicId.CopyDef }) {
                        srcI.ReplaceUses(srcSlot);

                        var store = new StoreVarInst(srcSlot, src);
                        store.InsertAfter(srcI);
                    }
                    //If this copy isn't on the same list, we need to sequentialize it
                    if (srcSlot != dstSlot) {
                        copiesToSeq.Add((dstSlot, srcSlot));
                    }
                } else if (src != dstSlot) {
                    //Source is a constant, can be copied directly into slot
                    var store = new StoreVarInst(dstSlot, src);
                    store.InsertAfter(copy);
                }
                copy.ReplaceWith(dstSlot);
            }
            if (copiesToSeq.Count > 0) {
                Sequentialize(copiesToSeq.AsSpan(), atEnd ? block.Last : block.First);
                copiesToSeq.Clear();
            }
        }
    }

    //The algorithm given in the paper has a typo in the comparison to insert temporary
    //copies, it should compare for inequality instead.
    //It also breaks if a source is used multiple times; that was fixed by only pushing to 
    //the pending queue if a dst is in loc.
    //More info: https://github.com/pfalcon/parcopy
    private void Sequentialize(ReadOnlySpan<(Variable Dst, Variable Src)> copies, Instruction insertPtBefore)
    {
        if (copies.Length == 1) {
            EmitCopy(copies[0].Dst, copies[0].Src);
            return;
        }
        var ready = new ArrayStack<Variable>();
        var pending = new ArrayStack<Variable>();
        var data = new Dictionary<Variable, (Variable? Pred, Variable? Loc)>();
        ref Variable? Loc(Variable var) => ref data.GetOrAddRef(var).Loc;
        ref Variable? Pred(Variable var) => ref data.GetOrAddRef(var).Pred;
        
        foreach (var (dst, src) in copies) {
            Loc(src) = src;
            Pred(dst) = src;
        }
        foreach (var (dst, src) in copies) {
            if (Loc(dst) == null) {
                ready.Push(dst); //dst is unused and can be overwritten
            } else {
                pending.Push(dst); //dst may need a temp
            }
        }
        while (true) {
            while (!ready.IsEmpty) {
                var dst = ready.Pop();
                var src = Pred(dst)!;
                var currLoc = Loc(src)!;
                EmitCopy(dst, currLoc);
                Loc(src) = dst;

                if (src == currLoc && Pred(src) != null) {
                    ready.Push(src);
                }
            }
            if (pending.IsEmpty) break;

            var pendingDst = pending.Pop();
            if (pendingDst != Loc(Pred(pendingDst)!)) {
                var tempSlot = new Variable(pendingDst.ResultType);
                EmitCopy(tempSlot, pendingDst);
                Loc(pendingDst) = tempSlot;
                ready.Push(pendingDst);
            }
        }
        void EmitCopy(Variable dest, Value src)
        {
            var store = new StoreVarInst(dest, src);
            store.InsertBefore(insertPtBefore);
        }
    }

    //Naive O(n^2) intersection test
    private bool SlowCorrectIntersect(MergeList listA, MergeList listB)
    {
        for (var na = listA.First; na != null; na = na.Next) {
            for (var nb = listB.First; nb != null; nb = nb.Next) {
                if (Dominates(na.Def, nb.Def) && Intersect(na, nb) && GetValue(na.Def) != GetValue(nb.Def)) {
                    return true;
                }
            }
        }
        return false;
    }

    private bool Intersect(MergeList listA, MergeList listB)
    {
        var dom = new ArrayStack<MergeNode>();
        var nodeA = listA.First;
        var nodeB = listB.First;

        //Traverse union of listA and listB, in order
        while (nodeA != null || nodeB != null) {
            var current = default(MergeNode);
            if (nodeA == null || (nodeB != null && ComesBefore(nodeB.Def, nodeA.Def))) {
                current = nodeB!;
                nodeB = nodeB!.Next;
            } else {
                current = nodeA;
                nodeA = nodeA.Next;
            }

            while (!dom.IsEmpty && !Dominates(dom.Top.Def, current.Def)) {
                dom.Pop();
            }
            if (!dom.IsEmpty && Interfere(current, dom.Top)) {
                return true;
            }
            dom.Push(current);
        }
        return false;
    }

    //Checks if `a` interferes (i.e., intersects and has a different value) with an already-visited variable.
    //This method also update ancestor information, and assumes that `b` dominates `a`.
    private bool Interfere(MergeNode a, MergeNode b)
    {
        a.EqualAncestorOut = null;
        if (a.List == b.List) {
            b = b.EqualAncestorOut!;
        }
        //Follow the chain of equal intersecting ancestors in the other set
        var anc = b;
        while (anc != null && !Intersect(anc, a)) {
            anc = anc.EqualAncestorIn;
        }
        if (b != null && GetValue(a.Def) != GetValue(b.Def)) {
            return anc != null;
        } else {
            //Update equal intersecting ancestor going up in the other set
            a.EqualAncestorOut = anc;
            return false;
        }
    }

    private Value? GetValue(Instruction inst)
    {
        var src = inst as Value;
        while (src is IntrinsicInst { Id: IntrinsicId.CopyDef } copy) {
            src = copy.Args[0];
        }
        return src;
    }

    //Checks if `a` and `b` have overlapping live ranges, assuming `a` dominates `b`.
    private bool Intersect(MergeNode a, MergeNode b)
    {
        Assert(Dominates(a.Def, b.Def));

        var (def, pos) = (a.Def, b.Def);
        var (liveIn, liveOut) = _liveness.GetLive(pos.Block);

        if (liveOut.Contains(def)) {
            //We should check if `def` is defined before `pos`, but that doesn't seem to matter...
            return true;
        }
        //If `def` is defined or liveIn in `pos`'s block, we need to check if it is used after `pos`
        if (def.Block == pos.Block || liveIn.Contains(def)) {
            while ((pos = pos.Next) != null) {
                if (pos.Operands.ContainsRef(def)) {
                    return true;
                }
            }
        }
        return false;
    }
    
    private bool Dominates(Instruction parent, Instruction child)
    {
        return parent.Block == child.Block 
            ? ComesBefore(parent, child)
            : _domTree.Dominates(parent.Block, child.Block);
    }
    //Checks if `a` is defined before `b` if they are on the same block, 
    //or based on the pre order dfs index of the dom tree. 
    private bool ComesBefore(Instruction a, Instruction b)
    {
        if (a.Block != b.Block) {
            return _domTree.GetPreIndex(a.Block) < _domTree.GetPreIndex(b.Block);
        }
        for (; a != null; a = a.Prev!) {
            if (a == b) return false;
        }
        return true;
    }

    private void AddMergeNode(MergeList list, Instruction def)
    {
        var node = new MergeNode() { Def = def, List = list };
        _mergeNodes.Add(def, node); //there can't be an existing node
        MergeLists(list, node);
    }
    private MergeList GetMergeList(Instruction def)
    {
        if (!_mergeNodes.TryGetValue(def, out var node)) {
            var list = new MergeList();
            _mergeNodes[def] = node = new MergeNode() { Def = def, List = list };
            list.First = node;
        }
        return node.List;
    }

    //Merges two lists while maintaining ascending pre dfs order of the dom tree
    //TODO-OPT: we could speed this using a sorted tree
    private void MergeLists(MergeList listA, MergeList listB)
    {
        MergeLists(listA, listB.First);
        listB.First = null;
    }
    private void MergeLists(MergeList listA, MergeNode? other)
    {
        if (listA.First == null) {
            listA.First = other;
            for (; other != null; other = other.Next) {
                other.List = listA;
            }
            return;
        }
        var prev = default(MergeNode);
        var curr = listA.First;

        while (other != null) {
            if (ComesBefore(other.Def, curr.Def)) {
                //Insert `other` before `curr`
                if (prev != null) {
                    prev.Next = other;
                } else {
                    listA.First = other;
                }
                var next = other.Next;
                other.Next = curr;
                other.List = listA;
                //Advance other
                prev = other;
                other = next;
            } else if (curr.Next != null) {
                prev = curr;
                curr = curr.Next!; //Advance curr
            } else {
                curr.Next = other; //Append other on end
                for (; other != null; other = other.Next) {
                    other.List = listA;
                }
                break;
            }
        }
    }

    //A list of variables that can be coalesced together, sorted based on the pre dfs order of the dom tree. 
    //This is refered as "congruence class" in the paper, and "merge set" in the SSA book.
    class MergeList
    {
        public MergeNode? First;
        public Variable? Slot;

        public Variable GetSlot() => Slot ??= new Variable(First!.Def.ResultType);

        public override string ToString()
        {
            var sb = new StringBuilder("[");
            //Find the sym table (looping because some insts will be removed from the method)
            var symTable = default(SymbolTable)!;
            for (var node = First; node != null && symTable == null; node = node.Next) {
                symTable = node.Def.Block.Method.GetSymbolTable();
            }
            for (var node = First; node != null; node = node.Next) {
                sb.Append(node == First ? "" : ", ");
                sb.Append(symTable.GetName(node.Def));
            }
            return sb.Append("]").ToString();
        }
    }
    class MergeNode
    {
        public MergeNode? Next;
        public MergeList List = null!;
        public Instruction Def = null!;

        public MergeNode? EqualAncestorIn; //Nearest ancestor that has the same value and intersects with this def
        public MergeNode? EqualAncestorOut; //Same as above, but in the other set
    }
}