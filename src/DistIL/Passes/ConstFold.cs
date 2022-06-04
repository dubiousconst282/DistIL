namespace DistIL.Passes;

using DistIL.IR;

/// <summary> Implements constant folding, and operand sorting for commutative instructions. </summary>
public class ConstFold : MethodPass
{
    public override void Run(MethodTransformContext ctx)
    {
        bool needsAnotherPass = true;
        while (needsAnotherPass) {
            needsAnotherPass = false;

            foreach (var block in ctx.Method) {
                foreach (var inst in block) {
                    //swap opers of `const <op> value` if op is commutative
                    if (inst is BinaryInst { Left: Const, Right: not Const, IsCommutative: true } b) {
                        (b.Left, b.Right) = (b.Right, b.Left);
                    }
                    var newValue = Fold(inst);
                    if (newValue != null) {
                        inst.ReplaceWith(newValue);
                    }
                }
                //We need another pass if we fold a branch and a phi into a const.
                needsAnotherPass |= FoldBranch(block, ctx);
            }
        }
    }

    public static Value? Fold(Instruction inst)
    {
        return inst switch {
            BinaryInst b  => FoldBinary(b.Op, b.Left, b.Right),
            UnaryInst u   => FoldUnary(u.Op, u.Value),
            ConvertInst c => FoldConvert(c.Value, c.ResultType, c.CheckOverflow, c.SrcUnsigned),
            CompareInst c => FoldCompare(c.Op, c.Left, c.Right),
            _ => null
        };
    }

    private bool FoldBranch(BasicBlock block, MethodTransformContext ctx)
    {
        bool phiFolded = false;

        if (block.Last is BranchInst br && br.Cond is ConstInt c) {
            bool cond = c.Value != 0;
            block.SetBranch(cond ? br.Then : br.Else!);
            var removedBlock = cond ? br.Else! : br.Then;

            foreach (var phi in removedBlock.Phis()) {
                phi.RemoveArg(block, true);
                phiFolded = true; //TODO: only do this if phi was replaced by a const
            }
            ctx.InvalidateAll();
        }
        return phiFolded;
    }

    public static Value? FoldBinary(BinaryOp op, Value left, Value right)
    {
        switch (left, right, op) {
            case (ConstInt i1, ConstInt i2, _): {
                long v1 = i1.Value;
                long v2 = i2.Value;

                long? result = op switch {
                    BinaryOp.Add  => v1 + v2,
                    BinaryOp.Sub  => v1 - v2,
                    BinaryOp.Mul  => v1 * v2,
                    BinaryOp.SDiv => (v1, v2) is (_, 0) or (int.MinValue, -1) ? null : v1 / v2,
                    BinaryOp.UDiv => v2 == 0 ? null : (long)(i1.UValue / i2.UValue),
                    BinaryOp.SRem => (v1, v2) is (_, 0) or (int.MinValue, -1) ? null : v1 % v2,
                    BinaryOp.URem => v2 == 0 ? null : (long)(i1.UValue % i2.UValue),
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
                break;
            }
            case (ConstFloat f1, ConstFloat f2, _): {
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
                break;
            }
            //Identity property: x op 0, x * 1, x & ~0 = x
            case (_, ConstInt { Value: 0 }, BinaryOp.Add or BinaryOp.Sub or BinaryOp.Or or BinaryOp.Xor or BinaryOp.Shl or BinaryOp.Shra or BinaryOp.Shrl):
            case (_, ConstInt { Value: 1 }, BinaryOp.Mul):
            case (_, ConstInt { Value: ~0L }, BinaryOp.And): {
                return left;
            }
            //Multiplication property: x * 0, x & 0 = 0
            case (_, ConstInt { Value: 0 }, BinaryOp.Mul or BinaryOp.And): {
                return right;
            }
        }
        return null;
    }

    public static Value? FoldUnary(UnaryOp op, Value value)
    {
        return (op, value) switch {
            (UnaryOp.Neg, ConstInt v)   => ConstInt.Create(v.ResultType, -v.Value),
            (UnaryOp.Not, ConstInt v)   => ConstInt.Create(v.ResultType, ~v.Value),
            (UnaryOp.FNeg, ConstFloat v) => ConstFloat.Create(v.ResultType, -v.Value),
            _ => null
        };
    }

    public static Value? FoldConvert(Value srcValue, TypeDesc dstType, bool chkOverflow, bool srcUnsigned)
    {
        if (chkOverflow) {
            return null; //TODO
        }
        var simpleDstType = dstType.StackType;
        if (simpleDstType == StackType.Long) {
            simpleDstType = StackType.Int;
        }
        return (srcValue, simpleDstType) switch {
            (ConstInt c, StackType.Int)     => ConstInt.Create(dstType, c.Value),
            (ConstInt c, StackType.Float)   => ConstFloat.Create(dstType, srcUnsigned ? c.UValue : c.Value),
            (ConstFloat c, StackType.Int)   => ConstInt.Create(dstType, dstType.Kind.IsUnsigned() ? (long)(ulong)c.Value : (long)c.Value),
            (ConstFloat c, StackType.Float) => ConstFloat.Create(dstType, c.Value),
            _ => null
        };
    }

    public static Value? FoldCompare(CompareOp op, Value left, Value right)
    {
        bool? r = null;
        switch (left, right, op) {
            case (Const c1, Const c2, CompareOp.Eq or CompareOp.Ne): {
                r = c1.Equals(c2) ^ (op == CompareOp.Ne);
                break;
            }
            case (ConstInt i1, ConstInt i2, _): {
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
                break;
            }
            case (ConstFloat f1, ConstFloat f2, _): {
                int c = f1.Value.CompareTo(f2.Value);

                r = op switch {
                    CompareOp.FOeq => c == 0,
                    CompareOp.FOlt => c < 0,
                    CompareOp.FOgt => c > 0,
                    CompareOp.FOle => c <= 0,
                    CompareOp.FOge => c >= 0,
                    CompareOp.FUne => c != 0,
                    CompareOp.FUlt => !(c < 0),
                    CompareOp.FUgt => !(c > 0),
                    CompareOp.FUle => !(c <= 0),
                    CompareOp.FUge => !(c >= 0),
                    _ => null
                };
                break;
            }
            //x != 0 -> x  (if x type is int)
            case (_, ConstInt { IsInt: true, Value: 0 }, CompareOp.Ne): {
                return left;
            }
        }
        return r == null ? null : ConstInt.CreateI(r.Value ? 1 : 0);
    }
}