namespace DistIL.Passes;

using DistIL.IR;

public class MergeBlocks : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        bool changed = true;
        while (changed) {
            changed = false;

            foreach (var block in ctx.Method) {
                changed |= MergeSingleSucc(block);
            }
        }
    }

    //Merge blocks with single preds that jumps into them
    //  BB_01:
    //    ...
    //    goto BB_02
    //  BB_02: //preds=BB_01
    //    ...
    public static bool MergeSingleSucc(BasicBlock b1)
    {
        if (!(b1.Succs.Count == 1 && b1.Succs[0].Preds.Count == 1)) return false;
        var b2 = b1.Succs[0];
        //"b1: ...; goto b2" and not "b1: ...; goto b1"
        if (!(b1.Last is BranchInst br && br.Then == b2 && br.Then != b1)) return false;
        //Phi depends on control flow
        if (b2.First is PhiInst or GuardInst) return false;

        //Remove "goto b2" from b1
        b1.Last.Remove();
        //Move instructions in b2 to b1
        b2.MoveRange(b1, b1.Last, b2.First, b2.Last);

        //Forward edges
        b1.Succs.Remove(b2);
        foreach (var succ in b2.Succs) {
            succ.Preds.Remove(b2);
            b1.Connect(succ);
        }
        b2.ReplaceUses(b1);
        b2.Remove();
        return true;
    }
}