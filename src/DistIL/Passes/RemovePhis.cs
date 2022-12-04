namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.AsmIO;
using DistIL.IR.Intrinsics;

//This pass is based on "Revisiting Out-of-SSA Translation for Correctness, Code Quality, and Efficiency"
//by Boissinot et al. (https://hal.inria.fr/inria-00349925v1/document)
//
//The idea is to isolate phis (split live range of its dependencies) by inserting parallel copies 
//for each argument into their respective predecessor blocks, and for each phi result (to solve the lost copy/swap problem).
//Then, all non-interfering copies can be coalesced into the same "congruence class/merge list".
//
//The paper describes an efficient algorithm to check for interferences in two merge lists, with
//the notion of value interference (two variables with the same value don't interfere).
//It also describes an algorithm to convert parallel copies into a simple sequence of load/stores.

/// <summary> Replace all phi instructions with local variables. </summary>
public class RemovePhis : MethodPass
{
    MethodBody _method = null!;
    Dictionary<Instruction, MergeNode> _mergeNodes = new();
    Dictionary<MergeNode[], Variable> _slotVars = new();
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
        _slotVars.Clear();
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
                    var copy = CreateCopy(val, GetLastInst(pred), true);
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
            var copy = new IntrinsicInst(IRIntrinsic.CopyDef, source);
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
                var mergeList = new MergeNode[phi.NumArgs + 1];
                int index = 0;
                
                foreach (var arg in phi) {
                    if (arg.Value is Instruction argI) {
                        AddNode(argI);
                    }
                }
                AddNode(phi);

                Array.Resize(ref mergeList, index);
                Array.Sort(mergeList, (a, b) => a == b ? 0 : ComesBefore(a.Def, b.Def) ? -1 : +1);

                void AddNode(Instruction inst)
                {
                    var node = new MergeNode() { Def = inst, List = mergeList };
                    _mergeNodes.Add(inst, node);
                    mergeList[index++] = node;
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

                if (dstList != srcList) {
                    MergeIfNotIntersecting(dstList, srcList);
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
                var dstSlot = GetSlotVar(copy);
                var src = copy.Args[0];

                if (src is Instruction srcI) {
                    var srcSlot = GetSlotVar(srcI);
                    //Copy source to its slot, and replace other uses with it
                    if (srcI.Block != null && !srcI.Is(IRIntrinsicId.CopyDef)) {
                        srcI.ReplaceUses(srcSlot);

                        var store = new StoreVarInst(srcSlot, src);
                        store.InsertAfter(srcI);
                    }
                    //If this copy isn't on the same list, we need to sequentialize it
                    if (srcSlot != dstSlot) {
                        copiesToSeq.Add((dstSlot, srcSlot));
                    }
                } else if (src != dstSlot && src is not Undef) {
                    //Source is a constant, can be copied directly into slot
                    var store = new StoreVarInst(dstSlot, src);
                    store.InsertAfter(copy);
                }
                copy.ReplaceWith(dstSlot);
            }
            if (copiesToSeq.Count > 0) {
                Sequentialize(copiesToSeq.AsSpan(), atEnd ? GetLastInst(block) : block.First);
                copiesToSeq.Clear();
            }
        }
    }

    private MergeNode[] GetMergeList(Instruction def)
    {
        if (!_mergeNodes.TryGetValue(def, out var node)) {
            _mergeNodes[def] = node = new MergeNode() { Def = def, List = new MergeNode[1] };
            node.List[0] = node;
        }
        return node.List;
    }
    private Variable GetSlotVar(Instruction def)
    {
        var list = _mergeNodes[def].List;
        return _slotVars.GetOrAddRef(list)
                ??= new Variable(GetListVarType(list));
    }

    private TypeDesc GetListVarType(MergeNode[] list)
    {
        //We must use the biggest type associated with the list, in order to avoid truncating values.
        //Consider the following:
        //  ushort x1; ...
        //  int x2; ...
        //  int res = phi x1, x2;
        //If GetSlotVar() is first called with x1 as argument, the list will be assigned 
        //to a new variable of ushort, which will truncate the value of x2.
        //We also consider interface and base types, demoting the result to a common ancestor type.
        var type = default(TypeDesc);
        foreach (var node in list) {
            if (GetValue(node.Def) is not ConstNull) {
                type = TypeDesc.GetCommonAssignableType(type, node.Def.ResultType);
            }
        }
        return type ?? PrimType.Object;
    }

    private static Instruction GetLastInst(BasicBlock block)
    {
        var inst = block.Last;
        if (inst.Prev is CompareInst { NumUses: 1 } cmp && cmp.Users().First() == inst) {
            return cmp;
        }
        return inst;
    }

    private bool MergeIfNotIntersecting(MergeNode[] listA, MergeNode[] listB)
    {
        //Note: It's very rare for two lists to interfere with each other (only ~4% do),
        //so we always preallocate the merged result.
        var dom = new ArrayStack<MergeNode>();
        var mergedNodes = new MergeNode[listA.Length + listB.Length];
        int indexA = 0, indexB = 0, indexC = 0;

        //Traverse union of listA and listB, in ascending def order
        while (indexA < listA.Length || indexB < listB.Length) {
            bool currIsB = 
                indexA >= listA.Length || 
                (indexB < listB.Length && ComesBefore(listB[indexB].Def, listA[indexA].Def));

            var curr = currIsB ? listB[indexB++] : listA[indexA++];

            //Check if the current def is interferes with a dominating one
            while (!dom.IsEmpty && !Dominates(dom.Top.Def, curr.Def)) {
                dom.Pop();
            }
            if (!dom.IsEmpty && Interferes(curr, dom.Top)) {
                return false;
            }
            dom.Push(curr);
            Debug.Assert(indexC == 0 || ComesBefore(mergedNodes[indexC - 1].Def, curr.Def));
            mergedNodes[indexC++] = curr;
        }

        //Update nodes and nearest ancestors to "max(in, out)" based on the pre-dfs index
        //(Is this all the unexplained "pre computation" in the paper?)
        foreach (var node in mergedNodes) {
            var ancIn = node.EqualAncestorIn;
            var ancOut = node.EqualAncestorOut;
            if (ancIn == null || (ancOut != null && ComesBefore(ancIn.Def, ancOut.Def))) {
                node.EqualAncestorIn = ancOut;
            }
            node.List = mergedNodes;
        }
        return true;
    }

    //Checks if `a` interferes (i.e., intersects and has a different value) with an already-visited variable.
    //This method also update ancestor information, and assumes that `b` dominates `a`.
    //TODO: This code seems to be broken, it will ocasionally return true for nodes that don't intersect at all.
    //      We should look into fixing it unless we endup implementing an "register allocator" which can deal with phis.
    //      The SSABook has an similar algorithm, and it looks simpler.
    private bool Interferes(MergeNode a, MergeNode b)
    {
        a.EqualAncestorOut = null;
        if (a.List == b.List) {
            b = b.EqualAncestorOut!;
            if (b == null) {
                return false;
            }
        }
        //Follow the chain of equal intersecting ancestors in the other set
        var anc = b;
        while (anc != null && !Intersects(anc.Def, a.Def)) {
            anc = anc.EqualAncestorIn;
        }

        if (GetValue(a.Def) != GetValue(b.Def)) {
            return anc != null;
        } else {
            //Update equal intersecting ancestor going up in the other set
            a.EqualAncestorOut = anc;
            return false;
        }
    }

    //Checks if `a` and `b` have overlapping live ranges, assuming `a` dominates `b`.
    private bool Intersects(Instruction a, Instruction b)
    {
        Debug.Assert(Dominates(a, b));
        return _liveness.IsLiveAfter(a, b);
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

    private Value? GetValue(Instruction inst)
    {
        Value? src = inst;
        while (src is Instruction currInst && currInst.Is(IRIntrinsicId.CopyDef)) {
            src = currInst.Operands[0];
        }
        return src;
    }

    //The algorithm given in the paper has a typo in the comparison to insert temporary copies,
    //it should compare for inequality instead: `pendingDst != Loc(Pred(pendingDst))`.
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

    class MergeNode
    {
        public Instruction Def = null!;

        //The "merge list" this node is in.
        //It contains all defs that can be coalesced together, sorted based on the pre dfs order of the dom tree. 
        //This is refered as "congruence class" in the paper, and "merge set" in the SSA book.
        public MergeNode[] List = null!;

        public MergeNode? EqualAncestorIn; //Nearest ancestor that has the same value and intersects with this def
        public MergeNode? EqualAncestorOut; //Same as above, but in the other set
    }
}