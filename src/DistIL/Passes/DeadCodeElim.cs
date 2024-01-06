namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR.Utils;

public class DeadCodeElim : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var changes = MethodInvalidations.None;
        var funcInfo = ctx.Compilation.GetAnalysis<GlobalFunctionEffects>();

        changes |= RemoveUnreachableBlocks(ctx.Method) ? MethodInvalidations.ControlFlow : 0;
        changes |= RemoveUnusedVars(ctx.Method) ? MethodInvalidations.DataFlow : 0;
        changes |= RemoveUselessCode(ctx.Method, funcInfo) ? MethodInvalidations.DataFlow : 0;

        return changes;
    }

    public static bool RemoveUnreachableBlocks(MethodBody method)
    {
        bool changed = false;
        var worklist = new DiscreteStack<BasicBlock>();

        // Mark reachable blocks with a depth first search
        worklist.Push(method.EntryBlock);

        while (worklist.TryPop(out var block)) {
            // (goto 1 ? T : F)  ->  (goto T)
            changed |= ConstFolding.FoldBlockBranch(block);

            // Remove empty try-finally regions
            if (block.First is GuardInst { Kind: GuardKind.Finally, Next: not GuardInst, HandlerBlock.First: ResumeInst } guard) {
                var regionAnalysis = new ProtectedRegionAnalysis(method); // this is not ideal but this transform should be quite rare

                foreach (var exitBlock in regionAnalysis.GetBlockRegion(block).GetExitBlocks()) {
                    exitBlock.SetBranch(exitBlock.Succs.First());
                }
                guard.Remove();
            }
            
            foreach (var succ in block.Succs) {
                worklist.Push(succ);
            }
        }

        // Sweep unreachable blocks
        foreach (var block in method) {
            if (worklist.WasPushed(block)) continue;

            // Remove incomming args from phis in reachable blocks
            foreach (var succ in block.Succs) {
                if (worklist.WasPushed(succ)) {
                    succ.RedirectPhis(block, newPred: null);
                }
            }
            block.Remove();
            changed = true;
        }
        return changed;
    }

    public static bool RemoveUselessCode(MethodBody method, GlobalFunctionEffects? funcInfo)
    {
        var worklist = new DiscreteStack<Instruction>();

        // Mark useful instructions
        foreach (var inst in method.Instructions()) {
            if (inst.SafeToRemove) continue;
            if (funcInfo != null && IsSafeToRemoveCall(inst, funcInfo)) continue;

            // Mark `inst` and its entire dependency chain
            worklist.Push(inst);

            while (worklist.TryPop(out var chainInst)) {
                foreach (var oper in chainInst.Operands) {
                    if (oper is Instruction operI) {
                        worklist.Push(operI);
                    }
                }
            }
        }

        // Sweep useless instructions
        bool changed = false;

        foreach (var inst in method.Instructions()) {
            if (!worklist.WasPushed(inst)) {
                inst.Remove();
                changed = true;
            }
            else if (inst is PhiInst phi) {
                RemoveTrivialPhi(phi, peel: true);
            }
        }
        return changed;
    }

    private static bool IsSafeToRemoveCall(Instruction inst, GlobalFunctionEffects funcInfo)
    {
        if (inst is not CallInst call || (call.IsVirtual && !call.InBounds)) return false;

        var effects = funcInfo.GetEffects(call.Method);

        return effects.IsPure;
    }

    // Remove phi-webs where all arguments have the same value
    public static void RemoveTrivialPhi(PhiInst phi, bool peel)
    {
        while (true) {
            var firstArg = phi.GetValue(0);

            for (int i = 1; i < phi.NumArgs; i++) {
                var arg = phi.GetValue(i);

                if (!arg.Equals(firstArg) && arg != phi) return;
            }
            phi.ReplaceWith(firstArg);

            if (peel && firstArg is PhiInst nextPhi) {
                phi = nextPhi;
            } else break;
        }
    }

    public static bool RemoveUnusedVars(MethodBody method)
    {
        bool changed = false;

        foreach (var slot in method.LocalVars()) {
            // Remove if unused, or if non-pinned and all uses are from stores
            if (slot.NumUses == 0 || (!slot.IsPinned && slot.Users().All(u => u is StoreInst or CilIntrinsic.MemSet))) {
                foreach (var user in slot.Users()) {
                    user.Remove();
                }
                slot.Remove();
                changed = true;
            }
        }
        return changed;
    }
}