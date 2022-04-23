namespace DistIL.Passes;

using DistIL.IR;

public class MergeBlocks : Pass
{
    public override void Transform(Method method)
    {
        bool changed = true;
        while (changed) {
            changed = false;

            foreach (var block in method) {
                changed |= MergeSingleSucc(block);
                changed |= ForwardJump(block);
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
        if (b2.First is PhiInst) return false;

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

    //Forwards unconditional jumps
    //  BB_01:
    //    ...
    //    goto BB_02
    //  BB_02: //preds=BB_01
    //    goto BB_03
    public static bool ForwardJump(BasicBlock b1)
    {
        if (!(b1.Preds.Count == 1 && b1.Succs.Count == 1)) return false;
        if (!(b1.First is BranchInst br)) return false;

        var pred = b1.Preds[0];
        var succ = b1.Succs[0];
        //Ensure that removing this block won't duplicate edges/make a ambiguous phi:
        //  goto cond ? B1 : B2
        //  B1: goto B3
        //  B2: goto B3
        //  B3: val = phi [B1 -> x, B2 -> y]
        //We can remove either B1 or B2, but not both.
        //TODO: we can still remove it (and dedup edges) if there's no phi in succ.
        if (succ.Preds.Contains(pred)) return false;
        pred.Reconnect(b1, succ);

        foreach (var (inst, operIdx) in b1.Uses.ToArray()) {
            var replBlock = inst is PhiInst ? pred : succ;
            inst.ReplaceOperand(operIdx, replBlock);
        }
        b1.Remove();
        return true;
    }
}