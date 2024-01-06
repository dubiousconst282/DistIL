namespace DistIL.Passes;

using DistIL.Analysis;

using NameStack = ArrayStack<(BasicBlock B, Value V)>;

/// <summary> Promotes non-exposed local variables to SSA. </summary>
public class SsaPromotion : IMethodPass
{
    Dictionary<PhiInst, LocalSlot> _phiDefs = new(); // phi -> variable
    Dictionary<LocalSlot, NameStack> _slotNameStacks = new();
    DominatorTree _domTree = null!;

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        InsertPhis(ctx);

        if (_slotNameStacks.Count == 0) {
            Debug.Assert(_phiDefs.Count == 0);
            return MethodInvalidations.None;
        }

        _domTree = ctx.GetAnalysis<DominatorTree>();
        _domTree.Traverse(preVisit: RenameBlock);

        RemoveTrivialPhis();

        _phiDefs.Clear();
        _slotNameStacks.Clear();
        _domTree = null!;
        return MethodInvalidations.DataFlow;
    }

    private void InsertPhis(MethodTransformContext ctx)
    {
        var worklist = new DiscreteStack<BasicBlock>();
        var phiAdded = new RefSet<BasicBlock>(); // blocks where a phi has been added
        var domFrontier = default(DominanceFrontier);

        foreach (var slot in ctx.Method.LocalVars()) {
            if (!CheckPromoteableAndFindStores(slot, worklist, out bool isGlobal)) continue;

            // Only need to insert phis for variables used across multiple blocks
            if (isGlobal) {
                domFrontier ??= ctx.GetAnalysis<DominanceFrontier>();

                // Recursively insert phis on the DF of each block in the worklist
                while (worklist.TryPop(out var block)) {
                    foreach (var dom in domFrontier.Of(block)) {
                        if (!phiAdded.Add(dom)) continue;

                        var phi = dom.InsertPhi(slot.Type);
                        _phiDefs.Add(phi, slot);

                        worklist.Push(dom);
                    }
                }
                phiAdded.Clear();
            }
            _slotNameStacks.Add(slot, new());
        }
    }

    private static bool CheckPromoteableAndFindStores(LocalSlot slot, DiscreteStack<BasicBlock> definingBlocks, out bool isGlobal)
    {
        isGlobal = false;

        if (slot.IsPinned || slot.IsHardExposed) {
            return false;
        }

        definingBlocks.Clear();

        foreach (var (user, operIdx) in slot.Uses()) {
            if (user is StoreInst && operIdx == 0) {
                definingBlocks.Push(user.Block);
            } else if (user is LoadInst) {
                isGlobal |= definingBlocks.Depth == 0 || definingBlocks.Top != user.Block;
            } else {
                return false;
            }
        }
        isGlobal |= definingBlocks.DiscreteCount >= 2;

        return true;
    }

    private void RenameBlock(BasicBlock block)
    {
        // Init phi defs
        foreach (var phi in block.Phis()) {
            if (_phiDefs.TryGetValue(phi, out var slot)) {
                PushDef(_slotNameStacks[slot], block, phi);
            }
        }
        // Promote load/stores
        foreach (var inst in block.NonPhis()) {
            if (inst is not MemoryInst { Address: LocalSlot slot }) continue;
            if (!_slotNameStacks.TryGetValue(slot, out var stack)) continue;

            // Update latest def
            if (inst is StoreInst store) {
                var value = StoreInst.Coerce(store.ElemType, store.Value, insertBefore: inst);
                PushDef(stack, block, value);
                inst.Remove();
            }
            // Replace load with latest def
            else {
                Debug.Assert(inst is LoadInst);
                inst.ReplaceWith(ReadDef(stack, block, slot));
            }
        }
        // Fill successors phis
        foreach (var succ in block.Succs) {
            foreach (var phi in succ.Phis()) {
                if (_phiDefs.TryGetValue(phi, out var slot)) {
                    // TODO: AddArg() is O(n), maybe rewrite all phis in a final pass
                    phi.AddArg(block, ReadDef(_slotNameStacks[slot], block, slot));
                }
            }
        }
    }

    private void RemoveTrivialPhis()
    {
        // Remove trivially useless phis
        foreach (var phi in _phiDefs.Keys) {
            if (!phi.Users().Any(u => u != phi)) {
                phi.Remove();
            } else {
                DeadCodeElim.RemoveTrivialPhi(phi, peel: false);
            }
        }
    }

    private void PushDef(NameStack stack, BasicBlock block, Value value)
    {
        // Avoid pushing duplicate defs for the same block
        if (!stack.IsEmpty && stack.Top.B == block) stack.Pop();

        stack.Push((block, value));
    }
    private Value ReadDef(NameStack stack, BasicBlock block, LocalSlot slot)
    {
        // Try find a name for this block, while purging out of scope entries.
        // This is simpler and maybe even faster than tracking and popping all names
        // in PostVisit, since our dominance checks are quite cheap.
        while (!stack.IsEmpty) {
            if (_domTree.Dominates(stack.Top.B, block)) {
                return stack.Top.V;
            }
            stack.Pop();
        }
        return new Undef(slot.Type);
    }
}