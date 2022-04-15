namespace DistIL.Passes;

using DistIL.IR;

public class SimplifyArithm : RewritePass
{
    protected override Value Transform(IRBuilder ib, Instruction inst)
    {
        var result = inst switch {
            BinaryInst      c => SimplifyBinary(ib, c),
            CompareInst     c => SimplifyCompare(ib, c),
            _ => null
        };
        return result ?? inst;
    }

    private Value? SimplifyBinary(IRBuilder ib, BinaryInst inst)
    {
        var (op, left, right) = (inst.Op, inst.Left, inst.Right);

        if (op is BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul) {
            var cl = left as ConstInt;
            var cr = right as ConstInt;

            bool isL0 = cl != null && cl.Value == 0;
            bool isR0 = cr != null && cr.Value == 0;
            bool has0 = isL0 || isR0;

            //x + 0,  0 + x,  x - 0  ->  x
            if ((has0 && op == BinaryOp.Add) || (isR0 && op == BinaryOp.Sub)) {
                return isL0 ? right : left;
            }
            //x * 0,  0 * x  ->  0
            if (has0 && op == BinaryOp.Mul) {
                return isL0 ? left : right;
            }
        }
        return null;
    }

    private Value? SimplifyCompare(IRBuilder ib, CompareInst inst)
    {
        var (op, left, right) = (inst.Op, inst.Left, inst.Right);

        if (left.ResultType.StackType is StackType.Int && right is ConstInt c1) {
            //x != 0  ->  x
            if (op is CompareOp.Ne && c1.Value == 0) {
                return left;
            }
        }
        return null;
    }
}