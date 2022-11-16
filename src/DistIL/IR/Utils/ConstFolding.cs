namespace DistIL.IR.Utils;

using DistIL.IR.Intrinsics;

/// <summary> Helper for folding instructions with constant operands. </summary>
public class ConstFolding
{
    public static Value? Fold(Instruction inst)
    {
        return inst switch {
            BinaryInst b    => FoldBinary(b.Op, b.Left, b.Right),
            UnaryInst u     => FoldUnary(u.Op, u.Value),
            ConvertInst c   => FoldConvert(c.Value, c.ResultType, c.CheckOverflow, c.SrcUnsigned),
            CompareInst c   => FoldCompare(c.Op, c.Left, c.Right),
            CallInst c      => FoldCall(c.Method, c.Args),
            IntrinsicInst c => FoldIntrinsic(c.Intrinsic, c.Args),
            _ => null
        };
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
                    BinaryOp.Shrl => v1 >>> (int)v2,
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

    public static Value? FoldCall(MethodDesc method, ReadOnlySpan<Value> args)
    {
        if (IsCoreLibMethod(method, out var methodDef) && AllConsts(args)) {
            var declType = methodDef.DeclaringType;

            object? result = (declType.Namespace, declType.Name, method.Name) switch {
                ("System", "Math" or "MathF", _) => FoldMathCall(methodDef, args.ToArray()),
                ("System", "String", _) => FoldStringCall(methodDef, args.ToArray()),
                _ => null
            };
            return result switch {
                double r => ConstFloat.Create(method.ReturnType, r),
                long r   => ConstInt.Create(method.ReturnType, r),
                string r => ConstString.Create(r),
                _ => null
            };
        }
        return null;
    }

    public static Value? FoldIntrinsic(IntrinsicDesc intrinsic, ReadOnlySpan<Value> args)
    {
        return intrinsic switch {
            CilIntrinsic c => FoldCilIntrinsic(c, args),
            _ => null
        };
    }

    private static Value? FoldCilIntrinsic(CilIntrinsic intrinsic, ReadOnlySpan<Value> args)
    {
        return intrinsic.Id switch {
            CilIntrinsicId.SizeOf 
                when args is [TypeDesc { Kind: >= TypeKind.Bool and <= TypeKind.Double } type] 
                => ConstInt.CreateI(type.Kind.BitSize() / 8),
            _ => null
        };
    }

    private static object? FoldMathCall(MethodDef method, Value[] args)
    {
#pragma warning disable format
        return (method.Name, args) switch {
            ("Sqrt",    [ConstFloat x]) => Math.Sqrt(x.Value),
            ("Cbrt",    [ConstFloat x]) => Math.Cbrt(x.Value),
            ("Log",     [ConstFloat x]) => Math.Log(x.Value),
            ("Log2",    [ConstFloat x]) => Math.Log2(x.Value),
            ("Log10",   [ConstFloat x]) => Math.Log10(x.Value),
            ("Exp",     [ConstFloat x]) => Math.Exp(x.Value),
            ("Floor",   [ConstFloat x]) => Math.Floor(x.Value),
            ("Ceiling", [ConstFloat x]) => Math.Ceiling(x.Value),
            ("Truncate",[ConstFloat x]) => Math.Truncate(x.Value),
            ("Abs",     [ConstFloat x]) => Math.Abs(x.Value),

            ("Sin",     [ConstFloat x]) => Math.Sin(x.Value),
            ("Cos",     [ConstFloat x]) => Math.Cos(x.Value),
            ("Tan",     [ConstFloat x]) => Math.Tan(x.Value),
            ("Asin",    [ConstFloat x]) => Math.Asin(x.Value),
            ("Acos",    [ConstFloat x]) => Math.Acos(x.Value),
            ("Atan",    [ConstFloat x]) => Math.Atan(x.Value),
            ("Asinh",   [ConstFloat x]) => Math.Asinh(x.Value),
            ("Acosh",   [ConstFloat x]) => Math.Acosh(x.Value),
            ("Atanh",   [ConstFloat x]) => Math.Atanh(x.Value),

            ("Atan2",   [ConstFloat x, ConstFloat y]) => Math.Atan2(x.Value, y.Value),
            ("Log",     [ConstFloat x, ConstFloat y]) => Math.Log(x.Value, y.Value),

            ("Pow",     [ConstFloat x, ConstFloat y]) => Math.Pow(x.Value, y.Value),
            ("Min",     [ConstFloat x, ConstFloat y]) => Math.Min(x.Value, y.Value),
            ("Max",     [ConstFloat x, ConstFloat y]) => Math.Max(x.Value, y.Value),

            ("CopySign",[ConstFloat x, ConstFloat y]) => Math.CopySign(x.Value, y.Value),

            ("Round",   [ConstFloat x])                         => Math.Round(x.Value),
            ("Round",   [ConstFloat x, ConstInt d])             => Math.Round(x.Value, (int)d.Value),
            ("Round",   [ConstFloat x, ConstInt d, ConstInt m]) => Math.Round(x.Value, (int)d.Value, (MidpointRounding)m.Value),

            //Abs() throws for T.MinValue, don't fold those.
            ("Abs",     [ConstInt { Value: not (long.MinValue or int.MinValue) } x]) => Math.Abs(x.Value),
            ("Min",     [ConstInt x, ConstInt y]) => Math.Min(x.Value, y.Value),
            ("Max",     [ConstInt x, ConstInt y]) => Math.Max(x.Value, y.Value),
            _ => null
        };
#pragma warning restore format
    }
    private static object? FoldStringCall(MethodDef methodDef, Value[] args)
    {
        return (methodDef.Name, args) switch {
            ("get_Length", [ConstString str]) 
                => (long)str.Value.Length,
            
            ("get_Chars", [ConstString str, ConstInt idx]) 
                when idx.Value >= 0 && idx.Value < str.Value.Length
                => (long)str.Value[(int)idx.Value],

            ("Concat", _)
                when args.All(a => a is ConstString)
                => string.Concat(args.Select(a => ((ConstString)a).Value)),

            ("Replace", [ConstString str, ConstString oldStr, ConstString newStr])
                => str.Value.Replace(oldStr.Value, newStr.Value),

            ("Replace", [ConstString str, ConstInt oldChar, ConstInt newChar])
                => str.Value.Replace((char)oldChar.Value, (char)newChar.Value),

            _ => null
        };
    }

    private static bool IsCoreLibMethod(MethodDesc method, out MethodDef def)
    {
        def = (method as MethodDefOrSpec)?.Definition!;
        return def != null && def.DeclaringType.Module == def.Module.Resolver.CoreLib;
    }
    private static bool AllConsts(ReadOnlySpan<Value> args)
    {
        foreach (var value in args) {
            if (value is not Const) {
                return false;
            }
        }
        return true;
    }
}