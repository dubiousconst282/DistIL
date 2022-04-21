namespace DistIL.Passes;

using System.Numerics;

using DistIL.IR;

public class SimplifyConds : Pass
{
    public override void Transform(Method method)
    {
        foreach (var block in method) {
            while (TransformSelect(block) || InvertIf(block));
        }
    }

    private bool TransformSelect(BasicBlock condBlock)
    {
        //Search for this pattern:
        //  BB_01:
        //    goto cond ? BB_03 : BB_04
        //  BB_04:
        //    goto BB_05
        //  BB_03:
        //    goto BB_05
        //  BB_05:
        //    int v6 = phi [BB_03 -> *], [BB_04 -> *]
        if (!(condBlock.Last is BranchInst { IsConditional: true } br)) return false;

        var b1 = br.Then;
        var b2 = br.Else;

        //Check if b1 and b2 are empty (only contain a goto to final block)
        if (!(b1.First is BranchInst && b2.First is BranchInst)) return false;
        //Check if they both branch to the same block
        if (!(b1.Succs.Count == 1 && b2.Succs.Count == 1 && b1.Succs[0] == b2.Succs[0])) return false;

        var finalBlock = b1.Succs[0]; //post dom of condBlock
        //Check if final block is only reachable from b1 or b2, and that it contains a phi
        if (!(finalBlock.Preds.Count == 2)) return false;

        //If the final block starts with a phi, try convert it to a select
        if (finalBlock.First is PhiInst && !CreateSelects(finalBlock, br.Cond, b1, b2)) return false;

        //Remove b1 and b2, they are redundant now
        condBlock.SetBranch(finalBlock);
        b1.Remove();
        b2.Remove();
        MergeBlocks.MergeSingleSucc(condBlock); //try merge condBlock with finalBlock

        return true;
    }

    private bool CreateSelects(BasicBlock block, Value cond, BasicBlock trueBlock, BasicBlock falseBlock)
    {
        //Check if all phis can be converted
        var repls = new List<Value>();
        var ib = new IRBuilder(delayed: true);

        foreach (var phi in block.Phis()) {
            var a1 = phi.GetArg(0);
            var a2 = phi.GetArg(1);
            Value? val =
                SelectConstInt(ib, cond, a1.Value, a2.Value);

            if (val == null) return false;
            repls.Add(val);
        }
        //Commit changes
        foreach (var (repl, phi) in repls.Zip(block.Phis())) {
            phi.ReplaceWith(repl, false);
        }
        ib.PrependInto(block);
        return true;
    }

    private Value? SelectConstInt(IRBuilder ib, Value cond, Value t, Value f)
    {
        if (!(t is ConstInt ct && f is ConstInt cf)) return null;
        if (!(ct.IsInt && cf.IsInt)) return null; //TODO: support for long

        long vt = ct.Value;
        long vf = cf.Value;
        long delta = Math.Abs(vt - vf);

        //cond ? (o + k) : o -> cond * k + o (where k = 0 or pow2)
        //cond ? 1 : 0 -> cond
        if (cond is CompareInst cmp && BitOperations.IsPow2(delta)) {
            int scale = BitOperations.Log2((ulong)delta);
            if (scale != 0) {
                cond = ib.CreateShl(cond, ConstInt.CreateI(scale));
            }
            if (vf == 0) {
                return cond;
            }
            bool inv = vf > vt;
            var offs = ConstInt.Create(ct.ResultType, vf);
            return ib.CreateBin(
                inv ? BinaryOp.Sub : BinaryOp.Add,
                inv ? offs : cond,
                inv ? cond : offs
            );
        }
        return null;
    }

    private bool InvertIf(BasicBlock block)
    {
        //goto x == 0 ? T : F  ->  goto x ? F : T
        //goto x != 0 ? T : F  ->  goto x ? T : F
        if (block.Last is BranchInst br && 
            br.Cond is CompareInst { Op: CompareOp.Eq or CompareOp.Ne, Right: ConstInt { Value: 0 } } cond)
        {
            bool inv = cond.Op == CompareOp.Eq;
            var newThen = inv ? br.Else : br.Then;
            var newElse = inv ? br.Then : br.Else;
            br.ReplaceWith(new BranchInst(cond.Left, newThen!, newElse!));
            return true;
        }
        return false;
    }
}