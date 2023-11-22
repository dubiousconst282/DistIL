namespace DistIL.Passes;

using System.Runtime.CompilerServices;

using DistIL.Analysis;

/// <summary> Promotes non-exposed local variables to SSA. </summary>
public class SsaPromotion : IMethodPass
{
    MethodBody _method = null!;
    Dictionary<LocalSlot, SlotInfo> _slotInfos = new();
    Dictionary<PhiInst, LocalSlot> _phiDefs = new(); // phi -> variable

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        _method = ctx.Method;
        var domTree = ctx.GetAnalysis<DominatorTree>(preserve: true);
        var domFrontier = ctx.GetAnalysis<DominanceFrontier>(preserve: true);

        InsertPhis(domFrontier);
        RenameDefs(domTree);

        _method = null!;
        _phiDefs.Clear();
        _slotInfos.Clear();

        return MethodInvalidations.DataFlow;
    }

    private void InsertPhis(DominanceFrontier domFrontier)
    {
        var killedVars = new RefSet<LocalSlot>();

        // Find definitions
        foreach (var block in _method) {
            foreach (var inst in block) {
                ref var slotInfo = ref GetAccessedSlotInfo(inst, out var slot);
                if (Unsafe.IsNullRef(ref slotInfo)) continue;

                if (inst is StoreInst) {
                    var worklist = slotInfo.AssignBlocks ??= new();
                    // Add parent block to the worklist, avoiding dupes
                    if (worklist.Count == 0 || worklist.Top != block) {
                        worklist.Push(block);
                    }
                    killedVars.Add(slot);
                }
                // If we are loading a variable that has not yet been assigned in this block, mark it as global
                else if (!killedVars.Contains(slot)) {
                    slotInfo.IsGlobal = true;
                }
            }
            killedVars.Clear();
        }

        var phiAdded = new RefSet<BasicBlock>(); // blocks where a phi has been added
        var processed = new RefSet<BasicBlock>(); // blocks already visited in worklist

        // Insert phis
        foreach (var (slot, info) in _slotInfos) {
            var worklist = info.AssignBlocks;

            // Avoid inserting phis for variables only alive in a single block (semi-pruned ssa)
            if (worklist == null || !info.IsGlobal) continue;

            // Initialize processed set (we do this to avoid keeping a whole HashSet for each variable)
            foreach (var def in worklist) {
                processed.Add(def);
            }
            // Recursively insert phis on the DF of each block in the worklist
            while (worklist.TryPop(out var block)) {
                foreach (var dom in domFrontier.Of(block)) {
                    if (!phiAdded.Add(dom)) continue;
                    
                    var phi = dom.InsertPhi(slot.Type);
                    _phiDefs.Add(phi, slot);

                    if (processed.Add(dom)) {
                        worklist.Push(dom);
                    }
                }
            }
            phiAdded.Clear();
            processed.Clear();
        }
    }

    private void RenameDefs(DominatorTree domTree)
    {
        var defDeltas = new ArrayStack<(BasicBlock B, ArrayStack<Value> DefStack)>();
        defDeltas.Push((null!, null!)); // dummy element so we don't need to check IsEmpty in RestoreDefs

        domTree.Traverse(
            preVisit: RenameBlock,
            postVisit: RestoreDefs
        );

        void RenameBlock(BasicBlock block)
        {
            // Init phi defs
            foreach (var phi in block.Phis()) {
                if (_phiDefs.TryGetValue(phi, out var slot)) {
                    PushDef(ref GetSlotInfo(slot), block, phi);
                }
            }
            // Promote load/stores
            foreach (var inst in block.NonPhis()) {
                ref var slotInfo = ref GetAccessedSlotInfo(inst, out var slot);
                if (Unsafe.IsNullRef(ref slotInfo)) continue;

                // Update latest def
                if (inst is StoreInst store) {
                    var value = StoreInst.Coerce(store.ElemType, store.Value, insertBefore: inst);
                    PushDef(ref slotInfo, block, value);
                    inst.Remove();
                }
                // Replace load with latest def
                else {
                    Debug.Assert(inst is LoadInst);
                    inst.ReplaceWith(ReadDef(ref slotInfo, slot));
                }
            }
            // Fill successors phis
            foreach (var succ in block.Succs) {
                foreach (var phi in succ.Phis()) {
                    if (_phiDefs.TryGetValue(phi, out var slot)) {
                        // TODO: AddArg() is O(n), maybe rewrite all phis in a final pass
                        phi.AddArg(block, ReadDef(ref GetSlotInfo(slot), slot));
                    }
                }
            }
        }
        void RestoreDefs(BasicBlock block)
        {
            // Restore def stack to what it was before visiting `block`
            while (defDeltas.Top.B == block) {
                defDeltas.Top.DefStack.Pop();
                defDeltas.Pop();
            }

            // Remove trivially useless phis
            foreach (var phi in block.Phis()) {
                if (!phi.Users().Any(u => u != phi)) {
                    phi.Remove();
                }
            }
        }
        // Helpers for R/W the def stack
        void PushDef(ref SlotInfo slotInfo, BasicBlock block, Value value)
        {
            var stack = slotInfo.DefStack ??= new();
            stack.Push(value);
            defDeltas.Push((block, stack));
        }
        Value ReadDef(ref SlotInfo slotInfo, LocalSlot slot)
        {
            var stack = slotInfo.DefStack;
            return stack != null && !stack.IsEmpty 
                ? stack.Top 
                : new Undef(slot.Type);
        }
    }

    private ref SlotInfo GetAccessedSlotInfo(Instruction? inst, out LocalSlot slot)
    {
        slot = ((inst as MemoryInst)?.Address as LocalSlot)!;

        if (slot != null && !slot.IsPinned) {
            ref var info = ref _slotInfos.GetOrAddRef(slot);

            if (info.CanPromote ??= !slot.IsExposed()) {
                return ref info;
            }
        }
        return ref Unsafe.NullRef<SlotInfo>();
    }
    private ref SlotInfo GetSlotInfo(LocalSlot slot)
    {
        return ref _slotInfos.GetRef(slot);
    }

    struct SlotInfo
    {
        public bool? CanPromote;
        public bool IsGlobal;
        public ArrayStack<BasicBlock>? AssignBlocks;
        public ArrayStack<Value>? DefStack;
    }
}