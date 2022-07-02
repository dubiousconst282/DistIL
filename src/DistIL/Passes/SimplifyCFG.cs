namespace DistIL.Passes;

using System.Numerics;

using DistIL.IR;

public class SimplifyCFG : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        bool changed = true;
        while (changed) {
            changed = false;

            foreach (var block in ctx.Method) {
                changed |= TransformBlock(block);
            }
        }
    }

    private bool TransformBlock(BasicBlock block)
    {
        bool changed = false;

        if (block.Last is BranchInst { IsConditional: true } br) {
            changed |= InvertCond(block, br);
            changed |= ConvertToBranchless(block, br);
        }
        if (block.Last is BranchInst { IsJump: true } jmp) {
            changed |= MergeWithSucc(block, jmp);
        }
        return changed;
    }

    private bool MergeWithSucc(BasicBlock block, BranchInst jmp)
    {
        //succ can't start with a phi/guard, nor loop on itself, and we must be its only predecessor
        if (!(jmp.Then is { HasHeader: false, Preds.Count: 1 } succ && succ != block)) return false;

        //Delete `block<->succ` edge
        block.DisconnectBranch(jmp);
        jmp.Remove();

        //Create edge to succ's branch
        if (succ.Last.IsBranch) {
            succ.DisconnectBranch(succ.Last, redirectPhisTo: block);
            block.ConnectBranch(succ.Last);
        }
        //Move code
        succ.MoveRange(block, block.Last, succ.First, succ.Last);
        succ.Remove();
        return true;
    }

    //goto x == 0 ? T : F  ->  goto x ? F : T
    //goto x != 0 ? T : F  ->  goto x ? T : F
    private bool InvertCond(BasicBlock block, BranchInst br)
    {
        if (br.Cond is CompareInst { Op: CompareOp.Eq or CompareOp.Ne, Right: ConstInt { Value: 0 } } cond) {
            br.Cond = cond.Left;
            if (cond.Op == CompareOp.Eq) {
                (br.Then, br.Else) = (br.Else!, br.Then);
            }
            return true;
        }
        return false;
    }

    //  BB_01: ... goto cond ? BB_03 : BB_04
    //  BB_04: goto BB_05
    //  BB_03: goto BB_05
    //  BB_05: int v6 = phi [BB_03 -> x], [BB_04 -> y]
    // -->
    // | int v6 = select(cond, x, y)
    // | int v6 = cond * (y - x) + x    for constants
    private bool ConvertToBranchless(BasicBlock condBlock, BranchInst br)
    {
        var b1 = br.Then;
        var b2 = br.Else!;

        //both branches must be empty
        if (!(b1.First is BranchInst && b2.First is BranchInst)) return false;
        //both branches must jump to the same block
        if (!(b1.Succs.Count == 1 && b2.Succs.Count == 1 && b1.Succs[0] == b2.Succs[0])) return false;

        var finalBlock = b1.Succs[0]; //post dom of condBlock
        //Check if final block is only reachable from b1 or b2
        if (!(finalBlock.Preds.Count == 2)) return false;

        //If the final block starts with a phi, try convert it to a select
        if (finalBlock.First is PhiInst && !CreateSelects(finalBlock, br.Cond!, b1, b2)) return false;

        //Remove b1 and b2, they are redundant now
        condBlock.SetBranch(finalBlock);
        b1.Remove();
        b2.Remove();

        return true;
    }

    private bool CreateSelects(BasicBlock block, Value cond, BasicBlock trueBlock, BasicBlock falseBlock)
    {
        //Check if all phis can be converted
        var repls = new List<Value>();
        var ib = new IRBuilder(delayed: true);

        foreach (var phi in block.Phis()) {
            var trueVal = phi.GetValue(trueBlock);
            var falseVal = phi.GetValue(falseBlock);
            Value? val =
                SelectConstInt(ib, cond, trueVal, falseVal);

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
}