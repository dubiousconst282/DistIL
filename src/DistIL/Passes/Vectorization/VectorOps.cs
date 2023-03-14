namespace DistIL.Passes.Vectorization;

using DistIL.IR.Utils;

internal enum VectorOp
{
    /// <summary> Null sentinel. </summary>
    Invalid,

    //Packing
    Splat, Pack,
    Load, Store,
    GetLane, SetLane,
    Shuffle,

    //Arithmetic
    Add, Sub, Mul, Div,
    And, Or, Xor,
    Shl, Shra, Shrl,
    Neg, Not,

    //Math
    Abs, Min, Max,
    Floor, Ceil, Round,
    Sqrt, Fmadd,
    RSqrt, Recip,

    //Conditional
    Select, ExtractMSB,
    CmpEq, CmpNe, CmpLt, CmpGt, CmpLe, CmpGe,

    //Casting
    I2F, F2I,
    SignExt, ZeroExt,
}

/// <summary> Helper for mapping and emission of vector instructions. </summary>
internal class VectorTranslator
{
    readonly ModuleResolver _resolver;
    readonly Dictionary<(VectorType, VectorOp), MethodDesc> _funcMap = new();
    readonly Dictionary<VectorType, TypeSpec> _vecTypes = new();

    public VectorTranslator(ModuleResolver resolver)
    {
        _resolver = resolver;
    }

    public Instruction EmitOp(IRBuilder builder, VectorType type, VectorOp op, params Value[] args)
    {
        var func = _funcMap.GetOrAddRef((type, op)) ??= FindFunc(type, op);
        return builder.CreateCall(func, args);
    }

    public TypeSpec GetActualType(VectorType type)
    {
        if (_vecTypes.TryGetValue(type, out var actualType)) {
            return actualType;
        }
        string ns = "System.Runtime.Intrinsics";
        string name = "Vector" + type.BitWidth + "`1";

        actualType = _resolver.CoreLib
            .FindType(ns, name, throwIfNotFound: true)
            .GetSpec(type.ElemType);

        return _vecTypes[type] = actualType;
    }
    public TypeDef GetBaseType(VectorType type)
    {
        string ns = "System.Runtime.Intrinsics";
        string name = "Vector" + type.BitWidth;

        return _resolver.CoreLib.FindType(ns, name, throwIfNotFound: true);
    }
    public TypeDesc GetShuffleIndexType(VectorType type)
    {
        return type.ElemKind switch {
            TypeKind.Single => PrimType.Int32,
            TypeKind.Double => PrimType.Int64,
            _ => type.ElemType
        };
    }

#pragma warning disable format
    public (VectorOp Op, TypeKind ScalarType) GetVectorOp(Instruction inst)
    {
        if (!VectorType.IsSupportedElemType(inst.ResultType) && inst is not CompareInst) {
            return default;
        }
        return inst switch {
            BinaryInst c => (GetBinaryOp(c.Op), inst.ResultType.Kind),
            CallInst c => (GetMathOp(c.Method), inst.ResultType.Kind),
            SelectInst c => (VectorOp.Select, inst.ResultType.Kind),
            CompareInst c => GetCompareOp(c),
            ConvertInst c => GetConvertOp(c),
            _ => default
        };
    }

    private static VectorOp GetBinaryOp(BinaryOp op)
    {
        return op switch {
            BinaryOp.Add => VectorOp.Add,
            BinaryOp.Sub => VectorOp.Sub,
            BinaryOp.Mul => VectorOp.Mul,
            //There's no HW support for integer vector division (x64/ARM),
            //so we might as well not even bother here.

            BinaryOp.And  => VectorOp.And,
            BinaryOp.Or   => VectorOp.Or,
            BinaryOp.Xor  => VectorOp.Xor,
            BinaryOp.Shl  => VectorOp.Shl,
            BinaryOp.Shra => VectorOp.Shra,
            BinaryOp.Shrl => VectorOp.Shrl,

            BinaryOp.FAdd => VectorOp.Add,
            BinaryOp.FSub => VectorOp.Sub,
            BinaryOp.FMul => VectorOp.Mul,
            BinaryOp.FDiv => VectorOp.Div,
            //Also no HW support for FRem.

            _ => default
        };
    }

    private static VectorOp GetMathOp(MethodDesc func)
    {
        var declType = func.DeclaringType;
        if (!(declType.IsCorelibType() && declType.Name is "Math" or "MathF")) {
            return default;
        }
        return func.Name switch {
            //TODO: Math.Min/Max() and vector instrs are not equivalent for floats (NaNs and fp stuff)
            "Abs"       => VectorOp.Abs,
            "Min"       => VectorOp.Min,
            "Max"       => VectorOp.Max,
            "Floor"     => VectorOp.Floor,
            "Ceiling"   => VectorOp.Ceil,
            "Round" when func.ParamSig.Count == 1
                        => VectorOp.Round,
            "Sqrt"      => VectorOp.Sqrt,
            "FusedMultiplyAdd"          => VectorOp.Fmadd,
            "ReciprocalSqrtEstimate"    => VectorOp.RSqrt, 
            "ReciprocalEstimate"        => VectorOp.Recip,
            _ => VectorOp.Invalid
        };
    }

    private static (VectorOp Op, TypeKind ScalarType) GetConvertOp(ConvertInst inst)
    {
        return (inst.SrcType, inst.DestType) switch {
            (TypeKind.Int32, TypeKind.Single) => (VectorOp.I2F, TypeKind.Single),
            (TypeKind.Single, TypeKind.Int32) => (VectorOp.F2I, TypeKind.Int32),
            _ => default
        };
    }

    private static (VectorOp Op, TypeKind ScalarType) GetCompareOp(CompareInst inst)
    {
        var typeL = inst.Left.ResultType.Kind;
        var typeR = inst.Right.ResultType.Kind;

        if (typeL.GetStorageType() != typeR.GetStorageType()) {
            return default;
        }
        //TODO: handle compare operand sign mismatch
        if (typeL.IsInt() && (inst.Op.IsSigned() != typeL.IsSigned() || inst.Op.IsSigned() != typeR.IsSigned())) {
            return default;
        }
        var op = inst.Op switch {
            CompareOp.Eq or CompareOp.FOeq => VectorOp.CmpEq,
            CompareOp.Ne or CompareOp.FUne => VectorOp.CmpNe,
            CompareOp.Slt or CompareOp.Ult or CompareOp.FOlt => VectorOp.CmpLt,
            CompareOp.Sgt or CompareOp.Ugt or CompareOp.FOgt => VectorOp.CmpGt,
            CompareOp.Sle or CompareOp.Ule or CompareOp.FOle => VectorOp.CmpLe,
            CompareOp.Sge or CompareOp.Uge or CompareOp.FOge => VectorOp.CmpGe,
            _ => default
        };
        return (op, typeL);
    }

    private MethodDesc FindFunc(VectorType type, VectorOp op)
    {
        var baseType = GetBaseType(type);

        var vecType = GetActualType(type);
        var unboundVec = vecType.GetUnboundSpec();
        var gm_Elem = GenericParamType.GetUnboundM(0);
        var gm_Vec = vecType.Definition.GetSpec(gm_Elem);

        return op switch {
            VectorOp.Splat      => FindDef("Create",        vecType,        type.ElemType),
            VectorOp.Pack       => FindDef("Create",        vecType,        Enumerable.Repeat((TypeSig)type.ElemType, type.Count).ToArray()),
            VectorOp.Load       => FindGen("LoadUnsafe",    gm_Vec,         gm_Elem.CreateByref()),
            VectorOp.Store      => FindGen("StoreUnsafe",   PrimType.Void,  gm_Vec, gm_Elem.CreateByref()),

            VectorOp.Add        => FindVOp("op_Addition"),
            VectorOp.Sub        => FindVOp("op_Subtraction"),
            VectorOp.Mul        => FindVOp("op_Multiply"),
            VectorOp.Div        => FindVOp("op_Division"),
            VectorOp.And        => FindVOp("op_BitwiseAnd"),
            VectorOp.Or         => FindVOp("op_BitwiseOr"),
            VectorOp.Xor        => FindVOp("op_ExclusiveOr"),

            VectorOp.Neg        => FindVOp("op_UnaryNegation", unary: true),
            VectorOp.Not        => FindVOp("op_OnesComplement", unary: true),

            VectorOp.Abs        => FindGen("Abs",           gm_Vec, gm_Vec),
            VectorOp.Min        => FindGen("Min",           gm_Vec, gm_Vec, gm_Vec),
            VectorOp.Max        => FindGen("Max",           gm_Vec, gm_Vec, gm_Vec),
            VectorOp.Sqrt       => FindGen("Sqrt",          gm_Vec, gm_Vec),

            VectorOp.CmpEq      => FindGen("Equals",            gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpLt      => FindGen("LessThan",          gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpGt      => FindGen("GreaterThan",       gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpLe      => FindGen("LessThanOrEqual",   gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpGe      => FindGen("GreaterThanOrEqual",gm_Vec, gm_Vec, gm_Vec),
            VectorOp.Select     => FindGen("ConditionalSelect", gm_Vec, gm_Vec, gm_Vec, gm_Vec),

            VectorOp.Shuffle    => FindDef("Shuffle",       vecType,        vecType.Definition.GetSpec(GetShuffleIndexType(type))),

            _ => throw new NotImplementedException()
        };
        MethodDesc FindDef(string funcName, TypeDesc retType, params TypeSig[] parTypes)
            => baseType.FindMethod(funcName, new MethodSig(retType, parTypes));

        MethodDesc FindGen(string funcName, TypeDesc retType, params TypeSig[] parTypes)
        {
            var sig = new MethodSig(retType, parTypes, numGenPars: 1);
            var def = (MethodDef)baseType.FindMethod(funcName, sig);
            return def.GetSpec(type.ElemType);
        }
        MethodDesc FindVOp(string funcName, bool unary = false)
        {
            var pars = unary ? new TypeSig[] { unboundVec } : new TypeSig[] { unboundVec, unboundVec };
            return vecType.FindMethod(funcName, new MethodSig(unboundVec, pars));
        }
    }
#pragma warning restore format
}