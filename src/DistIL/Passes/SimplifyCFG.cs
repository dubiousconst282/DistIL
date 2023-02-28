namespace DistIL.Passes;

public class SimplifyCFG : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        bool everChanged = false;

        //If-conversion can be made more effective by traversing blocks in post order,
        //since it will transform most deeply nested blocks first.
        ctx.Method.TraverseDepthFirst(postVisit: block => {
            bool changed = true;

            while (changed) {
                changed = false;
                changed |= ConvertToBranchless(block);
                changed |= MergeWithSucc(block);
                changed |= InvertCond(block);
                everChanged |= changed;
            }
        });

        return everChanged ? MethodInvalidations.ControlFlow : 0;
    }

    private static bool MergeWithSucc(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsJump: true } br) return false;

        //succ can't start with a phi/guard, nor loop on itself, and we must be its only predecessor
        if (br.Then is not { HasHeader: false, NumPreds: 1 } succ || succ == block) return false;

        succ.MergeInto(block, replaceBranch: true);
        return true;
    }

    //goto x == 0 ? T : F  ->  goto x ? F : T
    //goto x != 0 ? T : F  ->  goto x ? T : F
    private static bool InvertCond(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsConditional: true } br) return false;

        if (br.Cond is CompareInst { Op: CompareOp.Eq or CompareOp.Ne, Right: ConstInt { Value: 0 } } cond) {
            br.Cond = cond.Left;
            
            if (cond.Op == CompareOp.Eq) {
                (br.Then, br.Else) = (br.Else!, br.Then);
            }
            if (cond.NumUses == 0) {
                cond.Remove();
            }
            return true;
        }
        return false;
    }

    //Note that this currently only works with perfect diamond sub-graphs, 
    //and will fail to fully convert conditions like:
    //  (x >= 10 && x <= 20) || (x >= 50 && x <= 75)
    //This could be tackled with "path duplication" as described in chapter 16 of the SSA book.
    //
    //  Block: ...; goto cond ? Then : Else
    //  Then:  ...; goto Target
    //  Else:  ...; goto Target
    //  Target: int res = phi [Then -> x], [Else -> y]; ...
    // -->
    //  Block: ...; goto Target
    //  Target: int res = select cond ? x : y; ...
    private static bool ConvertToBranchless(BasicBlock block)
    {
        if (block.Last is not BranchInst { IsConditional: true } br) return false;
        
        //Match diamond branch
        if (br.Then is not { Last: BranchInst { IsJump: true, Then: var target } }) return false;
        if (br.Else is not { Last: BranchInst { IsJump: true, Then: var elseTarget } }) return false;

        //Limit to a single select per branch to avoid suboptimal codegen
        if (target != elseTarget || target.Phis().Count() is not 1) return false;
        if (!CanFlattenBranch(br.Then) || !CanFlattenBranch(br.Else)) return false;

        //Flatten branches
        br.Then.MergeInto(block, redirectSuccPhis: false);
        br.Else.MergeInto(block, redirectSuccPhis: false);
        block.SetBranch(target);

        //Rewrite phis with selects
        foreach (var phi in target.Phis()) {
            var select = CreateSelect(br, phi);
            select.InsertBefore(block.Last);

            if (phi.NumArgs <= 2) {
                phi.ReplaceWith(select);
            } else {
                phi.RemoveArg(br.Then, removeTrivialPhi: false);
                phi.RemoveArg(br.Else, removeTrivialPhi: false);
                phi.AddArg(block, select);
            }
        }
        return true;

        static bool CanFlattenBranch(BasicBlock block)
        {
            if (block.NumPreds >= 2 || block.HasHeader) return false;

            int cost = 0;

            foreach (var inst in block) {
                if (inst == block.Last) break;

                if (inst.HasSideEffects || ++cost > 2) {
                    return false;
                }
            }
            return true;
        }
        static Instruction CreateSelect(BranchInst br, PhiInst phi)
        {
            var valT = phi.GetValue(br.Then);
            var valF = phi.GetValue(br.Else!);

            if (br.Cond is CompareInst cond) {
                //select x ? 0 : y  ->  !x & y
                //select x ? 1 : y  ->   x | y
                //select x ? y : 0  ->   x & y
                //select x ? y : 1  ->  !x | y
                var (x, y) = (valT, valF) switch {
                    (ConstInt c, _) => (c, valF),
                    (_, ConstInt c) => (c, valT),
                    _ => default
                };
                if (x?.Value is 0 or 1 && (y.ResultType == PrimType.Bool || y is ConstInt { Value: 0 or 1 })) {
                    bool negCond = (x.Value == 0 && x == valT) || (x.Value != 0 && x == valF);

                    if (!negCond || cond.NumUses < 2) {
                        if (negCond) {
                            cond.Op = cond.Op.GetNegated();
                        }
                        var op = x.Value != 0 ? BinaryOp.Or : BinaryOp.And;
                        return new BinaryInst(op, cond, y);
                    }
                }
            }
            return new SelectInst(br.Cond!, valT, valF, phi.ResultType);
        }
    }
}