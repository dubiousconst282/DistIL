namespace DistIL.Passes;

using DistIL.Analysis;
using DistIL.IR.Utils;

public class DeadCodeElim : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var changes = MethodInvalidations.None;

        changes |= RemoveUnreachableBlocks(ctx.Method) ? MethodInvalidations.ControlFlow : 0;
        changes |= RemoveUselessCode(ctx.Method) ? MethodInvalidations.DataFlow : 0;

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

    public static bool RemoveUselessCode(MethodBody method)
    {
        var worklist = new DiscreteStack<Instruction>();

        // Mark useful instructions
        foreach (var inst in method.Instructions()) {
            if (inst.SafeToRemove) continue;

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
                PeelTrivialPhi(phi);
            }
        }
        return changed;
    }

    // Remove phi-webs where all arguments have the same value
    private static void PeelTrivialPhi(PhiInst phi)
    {
        while (true) {
            var firstArg = phi.GetValue(0);

            for (int i = 1; i < phi.NumArgs; i++) {
                var arg = phi.GetValue(i);

                if (!arg.Equals(firstArg) && arg != phi) return;
            }
            phi.ReplaceWith(firstArg);

            if (firstArg is PhiInst nextPhi) {
                phi = nextPhi;
            } else break;
        }
    }
}