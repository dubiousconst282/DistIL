namespace DistIL.Passes;

using DistIL.IR;

public class ConstFold : MethodPass
{
    private bool _phiArgRemoved; //whether a phi arg was removed and another pass must be run

    public override void Transform(Method method)
    {
        while (true) {
            _phiArgRemoved = false;
            base.Transform(method);
            if (!_phiArgRemoved) break;
        }
    }

    protected override Value Transform(IRBuilder ib, Instruction inst)
    {
        var result = inst switch {
            BinaryInst b 
                => FoldBinary(b.Op, b.Left, b.Right),
            ConvertInst c when !c.CheckOverflow
                => FoldConvert(c.Value, c.ResultType),
            CompareInst c
                => FoldCompare(c.Op, c.Left, c.Right),
            _ => null
        };
        return result ?? inst;
    }
    protected override void LeaveBlock(BasicBlock block)
    {
        if (block.Last is BranchInst br && br.Cond is ConstInt c) {
            bool cond = c.Value != 0;
            block.SetBranch(cond ? br.Then : br.Else!);
            var removedBlock = cond ? br.Else! : br.Then;

            foreach (var phi in removedBlock.Phis()) {
                phi.RemoveArg(block, true);
                _phiArgRemoved = true;
            }
        }
    }

    public static Value? FoldBinary(BinaryOp op, Value left, Value right)
    {
        if (left is ConstInt i1 && right is ConstInt i2) {
            long v1 = i1.Value;
            long v2 = i2.Value;

            long? result = op switch {
                BinaryOp.Add  => v1 + v2,
                BinaryOp.Sub  => v1 - v2,
                BinaryOp.Mul  => v1 * v2,
                BinaryOp.SDiv => v2 == 0 ? null : v1 / v2,
                BinaryOp.UDiv => v2 == 0 ? null : (long)((ulong)v1 / (ulong)v2),
                BinaryOp.SRem => v2 == 0 ? null : v1 % v2,
                BinaryOp.URem => v2 == 0 ? null : (long)((ulong)v1 % (ulong)v2),
                BinaryOp.And  => v1 & v2,
                BinaryOp.Or   => v1 | v2,
                BinaryOp.Xor  => v1 ^ v2,
                BinaryOp.Shl  => v1 << (int)v2,
                BinaryOp.Shra => v1 >> (int)v2,
                BinaryOp.Shrl => (long)((ulong)v1 >> (int)v2),
                _ => null
            };
            if (result != null) {
                bool isUnsigned = (i1.IsUnsigned && i2.IsUnsigned) || op is BinaryOp.UDiv or BinaryOp.URem;
                var resultType = i1.IsLong || i2.IsLong
                    ? (isUnsigned ? PrimType.UInt64 : PrimType.Int64)
                    : (isUnsigned ? PrimType.UInt32 : PrimType.Int32);
                return ConstInt.Create(resultType, result.Value);
            }
        }
        //
        else if (left is ConstFloat f1 && right is ConstFloat f2) {
            double v1 = f1.Value;
            double v2 = f2.Value;

            double? result = op switch {
                BinaryOp.FAdd => v1 + v2,
                BinaryOp.FSub => v1 - v2,
                BinaryOp.FMul => v1 * v2,
                BinaryOp.FDiv => v1 / v2,
                BinaryOp.FRem => v1 % v2,
                _ => null
            };
            if (result != null) {
                var resultType = f1.IsDouble || f2.IsDouble ? PrimType.Double : PrimType.Single;
                return ConstFloat.Create(resultType, result.Value);
            }
        }
        return null;
    }

    public static Value? FoldConvert(Value srcValue, RType dstType)
    {
        var srcType = srcValue.ResultType;

        if (srcValue is ConstInt ci) {
            if (srcType.Kind.IsInt() && dstType.Kind.IsInt()) {
                return ConstInt.Create(dstType, ci.Value);
            }
        }
        return null;
    }

    private Value? FoldCompare(CompareOp op, Value left, Value right)
    {
        if (left is Const c1 && right is Const c2) {
            bool? r = null;

            if (op is CompareOp.Eq or CompareOp.Ne) {
                r = c1.Equals(c2) ^ (op == CompareOp.Ne);
            }
            //
            else if (left is ConstInt i1 && right is ConstInt i2) {
                int sc = i1.Value.CompareTo(i2.Value);
                int uc = i1.UValue.CompareTo(i2.UValue);

                r = op switch {
                    CompareOp.Slt => sc < 0,
                    CompareOp.Sgt => sc > 0,
                    CompareOp.Sle => sc <= 0,
                    CompareOp.Sge => sc >= 0,
                    CompareOp.Ult => uc < 0,
                    CompareOp.Ugt => uc > 0,
                    CompareOp.Ule => uc <= 0,
                    CompareOp.Uge => uc >= 0,
                    _ => null
                };
            }
            return r == null ? null : ConstInt.CreateI(r.Value ? 1 : 0);
        }
        return null;
    }
}