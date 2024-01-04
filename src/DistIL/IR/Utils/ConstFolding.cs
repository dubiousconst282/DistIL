namespace DistIL.IR.Utils;

/// <summary> Helper for folding instructions with constant operands. </summary>
public class ConstFolding
{
    public static Value? Fold(Value inst)
    {
        return inst switch {
            BinaryInst b    => FoldBinary(b.Op, b.Left, b.Right),
            UnaryInst u     => FoldUnary(u.Op, u.Value),
            ConvertInst c   => FoldConvert(c.Value, c.ResultType, c.CheckOverflow, c.SrcUnsigned),
            CompareInst c   => FoldCompare(c.Op, c.Left, c.Right),
            CallInst c      => FoldCall(c.Method, c.Args),
            IntrinsicInst c => FoldIntrinsic(c),
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
            // x op 0, x * 1, x & ~0 = x
            case (_, ConstInt { Value: 0 }, BinaryOp.Add or BinaryOp.Sub or BinaryOp.Or or BinaryOp.Xor or BinaryOp.Shl or BinaryOp.Shra or BinaryOp.Shrl):
            case (_, ConstInt { Value: 1 }, BinaryOp.Mul):
            case (_, ConstInt { Value: ~0L }, BinaryOp.And): {
                return left;
            }
            // x * 0, x & 0 = 0
            case (_, ConstInt { Value: 0 }, BinaryOp.Mul or BinaryOp.And): {
                return right;
            }
            // x - x, x ^ x = 0
            case (_, _, BinaryOp.Sub or BinaryOp.Xor) when left.Equals(right): {
                return ConstInt.Create(left.ResultType, 0);
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
        if (srcValue.ResultType == dstType) {
            return srcValue;
        }
        if (chkOverflow) {
            return null; // TODO
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
            // (x != 0) -> x  where x: bool
            // This is not valid according to ECMA, but we'll assume that all bools are 0/1. 
            case ({ ResultType.Kind: TypeKind.Bool }, ConstInt { Value: 0 }, CompareOp.Ne): {
                return left;
            }
            // Cheat for thread checks in Linq iterators, blocking SROA.
            case (LoadInst { Address: FieldAddrInst { Field.Name: "_threadId" or "_state", Field: var fld, Obj: NewObjInst } }, _, _): {
                if (fld.DeclaringType is not TypeDefOrSpec { Module.AsmName.Name: "System.Linq" }) break;

                if (op == CompareOp.Eq && right is CallInst { Method.Name: "get_CurrentManagedThreadId" }) {
                    r = true;
                } else if (op == CompareOp.Ne && right is ConstInt { Value: 0 } && fld.Name == "_state") {
                    r = false;
                }
                break;
            }
        }
        return r == null ? null : ConstInt.Create(PrimType.Bool, r.Value ? 1 : 0);
    }

    public static Value? FoldSelect(Value cond, Value ifTrue, Value ifFalse)
    {
        if (ifTrue.Equals(ifFalse)) {
            return ifTrue;
        }

        return FoldCondition(cond) switch {
            true => ifTrue,
            false => ifFalse,
            _ => null
        };
    }

    /// <summary> Attempts to fold a conditional branch/switch in the block.  (goto 1 ? T : F)  ->  (goto T) </summary>
    public static bool FoldBlockBranch(BasicBlock block)
    {
        if (block.Last is BranchInst { IsConditional: true } br && FoldCondition(br.Cond) is bool cond) {
            var (blockT, blockF) = cond ? (br.Then, br.Else!) : (br.Else!, br.Then);

            blockF.RedirectPhis(block, newPred: null);
            block.SetBranch(blockT);
            return true;
        }
        if (block.Last is SwitchInst sw && (Fold(sw.TargetIndex) ?? sw.TargetIndex) is ConstInt caseIdx) {
            var targetBlock = sw.GetTarget((int)caseIdx.Value);

            foreach (var succ in sw.GetUniqueTargets()) {
                if (succ == targetBlock) continue;
                succ.RedirectPhis(block, newPred: null);
            }
            block.SetBranch(targetBlock);
            return true;
        }
        return false;
    }

    // TODO: evolve this using something like RangeAnalysis
    public static bool? FoldCondition(Value cond)
    {
        if (cond is CompareInst { Op: CompareOp.Ne, Right: ConstNull or ConstInt { Value: 0 } } cmp) {
            cond = cmp.Left;
        }
        if (cond is Instruction inst) {
            cond = Fold(inst) ?? cond;
        }

        return cond switch {
            ConstInt c => c.Value != 0,
            ConstNull => false,
            CilIntrinsic.Box { SourceType: { IsValueType: true, Name: not "Nullable`1" } } => true, // either throws or returns non-null 
            NewObjInst => true,
            _ => null,
        };
    }

    public static Value? FoldCall(MethodDesc method, ReadOnlySpan<Value> args)
    {
        if (IsCoreLibMethod(method, out var methodDef)) {
            var declType = methodDef.DeclaringType;

            object? result = (declType.Namespace, declType.Name, method.Name, AllConsts(args)) switch {
                ("System", "Math" or "MathF", _, true)
                    => FoldMathCall(methodDef, args.ToArray()),

                ("System", "String", _, true)
                    => FoldStringCall(methodDef, args.ToArray()),

                ("System", "Type", "op_Equality", false)
                    => FoldTypeEquality(args[0], args[1]),

                ("System.Runtime.CompilerServices", "RuntimeHelpers", "IsReferenceOrContainsReferences", _)
                    => method.GenericParams[0].IsRefOrContainsRefs(),

                _ => null
            };
            return result switch {
                double r => ConstFloat.Create(method.ReturnType, r),
                long r   => ConstInt.Create(method.ReturnType, r),
                bool r   => ConstInt.Create(method.ReturnType, r ? 1 : 0),
                string r => ConstString.Create(r),
                _ => null
            };
        }
        return null;
    }

    // bool Type.op_Equality(Type a, Type b)
    private static bool? FoldTypeEquality(Value arg1, Value arg2)
    {
        var typeA = UnwrapType(arg1);
        var typeB = UnwrapType(arg2);

        if (typeA == null || typeB == null) return null;
        if ((typeA.IsUnboundGeneric || typeB.IsUnboundGeneric) && typeA != typeB) return null;

        return typeA == typeB;

        static TypeDesc? UnwrapType(Value obj)
        {
            if (obj is not CallInst call) return null;

            if (call.Method.Name == "GetTypeFromHandle" && call.Method.DeclaringType.IsCorelibType(typeof(Type))) {
                return (call.Args[0] as CilIntrinsic.LoadHandle)?.StaticArgs[0] as TypeDesc;
            }
            if (call.Method.Name == "GetType" && call.Method.DeclaringType == PrimType.Object) {
                return TypeUtils.HasConcreteType(call.Args[0]) ? call.Args[0].ResultType : null;
            }
            return null;
        }
    }

    public static Value? FoldIntrinsic(IntrinsicInst intrin)
    {
        if (intrin is CilIntrinsic.SizeOf { ObjType.Kind: >= TypeKind.Bool and <= TypeKind.Double and var type }) {
            return ConstInt.CreateI(type.BitSize() / 8);
        }
        if (intrin is CilIntrinsic.AsInstance asi) {
            return TypeUtils.CheckCast(asi.Args[0], asi.DestType) switch {
                true => asi.Args[0],
                false => ConstNull.Create(),
                _ => null,
            };
        }
        if (intrin is CilIntrinsic.CastClass cast) {
            return TypeUtils.CheckCast(cast.Args[0], cast.DestType) switch {
                true => cast.Args[0],
                false => ConstNull.Create(),
                _ => null,
            };
        }
        return null;
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

            // Abs() throws for T.MinValue, don't fold those.
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
        return def != null && def.DeclaringType.IsCorelibType();
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