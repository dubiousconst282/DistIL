namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR;

/// <summary> Replace all phi instructions with local variables. </summary>
//This pass is based on the techniques described in
//"Revisiting Out-of-SSA Translation for Correctness, Code Quality, and Efficiency" by Boissinot et al.
//(https://hal.inria.fr/inria-00349925v1/document)
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
        _domTree = ctx.GetAnalysis<DominatorTree>();

        IsolatePhis();

        _liveness = ctx.GetAnalysis<LivenessAnalysis>();
        CoalescePhis();
        CoalesceCopies();
        ResolveSlots();

        //Cleanup
        _mergeNodes.Clear();
        _copies.Clear();
        _domTree = null!;
        _liveness = null!;
    }

    //Insert copies for each phi argument at the end of the predecessor block, 
    //and for the result after all other phis.
    private void IsolatePhis()
    {
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
        }

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
                    Console.WriteLine($"Merge {copy} - {src}");
                    MergeLists(dstList, srcList);
                } else if (dstList != srcList) {
                    Console.WriteLine($"Intersect: {copy} - {src}");
                }
            }
        }
    }

    //Copy registers into slots of their corresponding merge list and sequentialize copies
    private void ResolveSlots()
    {
        DistIL.Passes.Utils.NamifyIR.Run(_method);
        foreach (var node in _mergeNodes.Values.DistinctBy(n => n.List)) {
            Console.WriteLine(node.List);
        }

        var copiesToSeq = new List<(Variable Dst, Variable Src)>(); //temp list of vars that need to be sequentialized

        //Blocks with no copies have no phis, so this is fine
        foreach (var (block, (phiCopies, argCopies)) in _copies) {
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
                    //Copy source to its list slot, and replace other uses with it
                    if (srcI.Block != null && srcI is not IntrinsicInst { Id: IntrinsicId.CopyDef }) {
                        srcI.ReplaceUses(srcSlot);

                        var store = new StoreVarInst(srcSlot, src);
                        store.InsertAfter(srcI);
                    }
                    //If this copy isn't on the same list we need to sequentialize it
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
    private void Sequentialize(ReadOnlySpan<(Variable, Variable)> copies, Instruction insertPtBefore)
    {
        //TODO-OPT: don't allocate this many things
        var ready = new ArrayStack<Variable>();
        var pending = new ArrayStack<Variable>();
        var loc = new Dictionary<Variable, Variable>();
        var pred = new Dictionary<Variable, Variable>();
        
        foreach (var (dst, src) in copies) {
            loc[src] = src;
            pred[dst] = src;
        }
        foreach (var (dst, src) in copies) {
            if (!loc.ContainsKey(dst)) {
                ready.Push(dst); //dst is unused and can be overwritten
            } else {
                pending.Push(dst); //dst may need a temp
            }
        }
        while (true) {
            while (!ready.IsEmpty) {
                var dst = ready.Pop();
                var src = pred[dst];
                var currLoc = loc[src];
                EmitCopy(dst, currLoc);
                loc[src] = dst;

                if (src == currLoc && pred.ContainsKey(src)) {
                    ready.Push(src);
                }
            }
            if (pending.IsEmpty) break;

            var pendingDst = pending.Pop();
            if (pendingDst != loc[pred[pendingDst]]) {
                var tempSlot = new Variable(pendingDst.ResultType);
                EmitCopy(tempSlot, pendingDst);
                loc[pendingDst] = tempSlot;
                ready.Push(pendingDst);
            }
        }
        void EmitCopy(Variable dest, Value src)
        {
            var store = new StoreVarInst(dest, src);
            store.InsertBefore(insertPtBefore);
        }
    }

    private bool Intersect(MergeList listA, MergeList listB)
    {
        var dom = new ArrayStack<MergeNode>();
        var nodeA = listA.First;
        var nodeB = listB.First;

        //Traverse listA and listB as if they were merged, in order
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
            if (!dom.IsEmpty && Intersect(dom.Top, current)) {
                return true;
            }
            dom.Push(current);
        }
        return false;
    }

    //Checks if `a` and `b` have overlapping live ranges
    private bool Intersect(MergeNode a, MergeNode b)
    {
        Assert(Dominates(a.Def, b.Def));
        //Since we expect `a` to dominate `b`, we only need to check if `a` is live at `b`
        return _liveness.IsLiveAt(a.Def, b.Def);
    }
    //Checks if `a` dominates `b`.
    private bool Dominates(Instruction a, Instruction b)
    {
        return a.Block == b.Block 
            ? ComesBefore(a, b)
            : _domTree.Dominates(a.Block, b.Block);
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

    private Value? GetActualValue(Instruction inst)
    {
        var src = inst as Value;
        while (src is IntrinsicInst { Id: IntrinsicId.CopyDef } copy) {
            src = copy.Args[0];
        }
        return src;
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
    }
}