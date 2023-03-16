namespace DistIL.Passes.Vectorization;

using System.Numerics;

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

    //Internal
    _Sum, _PredicatedCount,
    _Bitcast0, _BitcastN = _Bitcast0 + (TypeKind.Double - TypeKind.SByte + 1),
}

/// <summary> Helps with mapping and emission of vector instructions. </summary>
internal class VectorTranslator
{
    readonly ModuleResolver _resolver;
    readonly Dictionary<(VectorType, VectorOp), MethodDesc> _funcMap = new();
    readonly Dictionary<VectorType, TypeSpec> _vecTypes = new();

    public VectorTranslator(ModuleResolver resolver)
    {
        _resolver = resolver;
    }

    public Value EmitOp(IRBuilder builder, VectorType type, VectorOp op, params Value[] args)
    {
        switch (op) {
            case VectorOp.Load: {
                return builder.CreateLoad(args[0], GetActualType(type), PointerFlags.Unaligned);
            }
            case VectorOp.Store: {
                return builder.CreateStore(args[0], args[1], GetActualType(type), PointerFlags.Unaligned);
            }
            case VectorOp.Pack when args.All(a => a.Equals(args[0])): {
                return EmitOp(builder, type, VectorOp.Splat, args[0]);
            }
            case VectorOp.CmpNe: {
                var eqMask = EmitOp(builder, type, VectorOp.CmpEq, args);
                return EmitOp(builder, type, VectorOp.Not, eqMask);
            }
            case VectorOp._PredicatedCount: {
                Debug.Assert(args[0].ResultType == PrimType.Int32 && GetActualType(type) == args[1].ResultType);
                return builder.CreateAdd(args[0], EmitPopCount(builder, type, args[1]));
            }
            default: {
                var func = _funcMap.GetOrAddRef((type, op)) ??= FindFunc(type, op);
                return builder.CreateCall(func, args);
            }
        }
    }

    public Value EmitBitcast(IRBuilder builder, VectorType type, TypeKind newElemType, Value vector)
    {
        if (type.ElemKind == newElemType) {
            return vector;
        }
        Debug.Assert(newElemType is >= TypeKind.SByte and <= TypeKind.Double);
        var placeholderOp = VectorOp._Bitcast0 + (newElemType - TypeKind.SByte);
        var func = _funcMap.GetOrAddRef((type, placeholderOp)) ??= FindCastFn();

        return builder.CreateCall(func, vector);

        MethodDesc FindCastFn()
        {
            string name = "As" + newElemType.ToString();
            var funcDef = (MethodDef)GetBaseType(type).FindMethod(name);
            return funcDef.GetSpec(type.ElemType);
        }
    }

    public Value EmitReduction(IRBuilder builder, VectorType type, VectorOp op, Value vector)
    {
        //Summation has a dedicated helper: Vector.Sum()
        if (op == VectorOp.Add) {
            return EmitOp(builder, type, VectorOp._Sum, vector);
        }
        //For any other op, we use a divide and conquer algo, see https://stackoverflow.com/a/35270026
        //TODO: this is not optimal for ARM (https://stackoverflow.com/q/31197216)
        //
        //256:  var t1 = Vector128.Max(x.GetLower(), x.GetUpper());                               = max([a,  b,  c, d], [e,  f,  g,  h])
        //128:  var t2 = Vector128.Max(t1, Vector128.Shuffle(t1, Vector128.Create(2, 3, 0, 0)));  = max([ae, bf, ?, ?], [cg, dh, ?, ?])
        //64:   var t3 = Vector128.Max(t2, Vector128.Shuffle(t2, Vector128.Create(1, 1, 0, 0)));  = max([aecg, ?, ?, ?], [bfdh, ?, ?, ?])
        //32:   return t3.GetElement(0);
        int width = type.BitWidth;
        int scalarWidth = type.ElemKind.BitSize();
        
        Debug.Assert(scalarWidth >= 8 && width <= 256 && vector.ResultType == GetActualType(type));

        if (width >= 256) {
            var baseType = GetBaseType(type);
            type = new VectorType(type.ElemKind, type.Count / 2);
            vector = EmitOp(builder, type, op, EmitHalf("GetLower"), EmitHalf("GetUpper"));
            width = 128;

            Value EmitHalf(string name)
            {
                var func = (MethodDef)baseType.FindMethod(name);
                return builder.CreateCall(func.GetSpec(type.ElemType), vector);
            }
        }
        for (; width > scalarWidth; width /= 2) {
            var shuffInds = new ConstInt[type.Count];
            int halfWidth = width / (scalarWidth * 2);

            for (int i = 0; i < shuffInds.Length; i++) {
                shuffInds[i] = ConstInt.CreateI(halfWidth + i % halfWidth);
            }
            var shuffMaskType = new VectorType(GetShuffleIndexType(type), type.Count);
            var shuffMask = EmitOp(builder, shuffMaskType, VectorOp.Pack, shuffInds);
            var shuffledVec = EmitOp(builder, type, VectorOp.Shuffle, vector, shuffMask);
            vector = EmitOp(builder, type, op, vector, shuffledVec);
        }
        return EmitOp(builder, type, VectorOp.GetLane, vector, ConstInt.CreateI(0));
    }

    private Value EmitPopCount(IRBuilder builder, VectorType type, Value vector)
    {
        var m_PopCount = _resolver.Import(typeof(BitOperations))
            .FindMethod("PopCount", new MethodSig(PrimType.Int32, new TypeSig[] { PrimType.UInt32 }));

        //uiCA says movmskb+popcount takes ~1c, but there's probably a better way to do this.
        var mask = EmitOp(builder,
            new VectorType(TypeKind.Byte, type.BitWidth / 8),
            VectorOp.ExtractMSB, 
            EmitBitcast(builder, type, TypeKind.Byte, vector));

        int shiftAmount = BitOperations.Log2((uint)type.ElemKind.Size());
        return builder.CreateShrl(builder.CreateCall(m_PopCount, mask), ConstInt.CreateI(shiftAmount));
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

    public bool IsVectorType(TypeDesc type)
    {
        return type.IsCorelibType() && type.Name is "Vector128`1" or "Vector256`1";
    }

#pragma warning disable format
    public (VectorOp Op, TypeKind ElemType) GetVectorOp(Instruction inst)
    {
        if (!VectorType.IsSupportedElemType(inst.ResultType) && inst.ResultType != PrimType.Bool) {
            return default;
        }
        return inst switch {
            BinaryInst c => (GetBinaryOp(c.Op), inst.ResultType.Kind),
            UnaryInst c => (GetUnaryOp(c.Op), inst.ResultType.Kind),
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

    private static VectorOp GetUnaryOp(UnaryOp op)
    {
        return op switch {
            UnaryOp.Neg     => VectorOp.Neg,
            UnaryOp.FNeg    => VectorOp.Neg,
            UnaryOp.Not     => VectorOp.Not,
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
            "Sqrt"      => VectorOp.Sqrt,
            "Floor"     => VectorOp.Floor,
            "Ceiling"   => VectorOp.Ceil,
            //"Round" when func.ParamSig.Count == 1
            //            => VectorOp.Round,
            //"FusedMultiplyAdd"          => VectorOp.Fmadd,
            //"ReciprocalSqrtEstimate"    => VectorOp.RSqrt, 
            //"ReciprocalEstimate"        => VectorOp.Recip,
            _ => VectorOp.Invalid
        };
    }

    private static (VectorOp Op, TypeKind ElemType) GetConvertOp(ConvertInst inst)
    {
        return (inst.SrcType, inst.DestType) switch {
            (TypeKind.Int32, TypeKind.Single) => (VectorOp.I2F, TypeKind.Single),
            (TypeKind.Single, TypeKind.Int32) => (VectorOp.F2I, TypeKind.Int32),
            _ => default
        };
    }

    private static (VectorOp Op, TypeKind ElemType) GetCompareOp(CompareInst inst)
    {
        var op = inst.Op;
        var typeL = inst.Left.ResultType.Kind;
        var typeR = inst.Right.ResultType.Kind;

        //TODO: better handling for operand signess mismatch
        //This allows compares like (sge u16, 10), but not (uge, i32, 0)
        if (typeL.IsUnsigned() && inst.Right is ConstInt c && c.FitsInType(inst.Left.ResultType)) {
            typeR = typeL;

            if (typeL.IsSmallInt() && op.IsSigned()) {
                op = op.GetUnsigned();
            }
        }

        if (typeL.GetStorageType() != typeR.GetStorageType() ||
            op.IsSigned() != typeL.IsSigned() ||
            op.IsSigned() != typeR.IsSigned()
        ) {
            return default;
        }
        var vecOp = op switch {
            CompareOp.Eq or CompareOp.FOeq => VectorOp.CmpEq,
            CompareOp.Ne or CompareOp.FUne => VectorOp.CmpNe,
            CompareOp.Slt or CompareOp.Ult or CompareOp.FOlt => VectorOp.CmpLt,
            CompareOp.Sgt or CompareOp.Ugt or CompareOp.FOgt => VectorOp.CmpGt,
            CompareOp.Sle or CompareOp.Ule or CompareOp.FOle => VectorOp.CmpLe,
            CompareOp.Sge or CompareOp.Uge or CompareOp.FOge => VectorOp.CmpGe,
            _ => default
        };
        return (vecOp, typeL);
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
            VectorOp.Shuffle    => FindDef("Shuffle",       vecType,        vecType, vecType.Definition.GetSpec(GetShuffleIndexType(type))),
            VectorOp.GetLane    => FindGen("GetElement",    gm_Elem,        gm_Vec, PrimType.Int32),
            VectorOp.SetLane    => FindGen("WithElement",   gm_Vec,         gm_Vec, PrimType.Int32, gm_Elem),

            VectorOp.Add        => FindVOp("op_Addition"),
            VectorOp.Sub        => FindVOp("op_Subtraction"),
            VectorOp.Mul        => FindVOp("op_Multiply"),
            VectorOp.Div        => FindVOp("op_Division"),
            VectorOp.And        => FindVOp("op_BitwiseAnd"),
            VectorOp.Or         => FindVOp("op_BitwiseOr"),
            VectorOp.Xor        => FindVOp("op_ExclusiveOr"),

            VectorOp.Neg        => FindVOp("op_UnaryNegation", unary: true),
            VectorOp.Not        => FindVOp("op_OnesComplement", unary: true),

            VectorOp.Abs        => FindGen("Abs",               gm_Vec, gm_Vec),
            VectorOp.Min        => FindGen("Min",               gm_Vec, gm_Vec, gm_Vec),
            VectorOp.Max        => FindGen("Max",               gm_Vec, gm_Vec, gm_Vec),
            VectorOp.Sqrt       => FindGen("Sqrt",              gm_Vec, gm_Vec),
            
            VectorOp.Floor      => FindDef("Floor",             vecType, vecType),
            VectorOp.Ceil       => FindDef("Ceiling",           vecType, vecType),

            VectorOp.CmpEq      => FindGen("Equals",            gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpLt      => FindGen("LessThan",          gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpGt      => FindGen("GreaterThan",       gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpLe      => FindGen("LessThanOrEqual",   gm_Vec, gm_Vec, gm_Vec),
            VectorOp.CmpGe      => FindGen("GreaterThanOrEqual",gm_Vec, gm_Vec, gm_Vec),
            VectorOp.Select     => FindGen("ConditionalSelect", gm_Vec, gm_Vec, gm_Vec, gm_Vec),
            VectorOp.ExtractMSB => FindGen("ExtractMostSignificantBits", PrimType.UInt32, gm_Vec),

            VectorOp.F2I        => FindDef("ConvertToInt32",    vecType, vecType.Definition.GetSpec(PrimType.Single)),
            VectorOp.I2F        => FindDef("ConvertToSingle",   vecType, vecType.Definition.GetSpec(PrimType.Int32)),

            VectorOp._Sum       => FindGen("Sum",               gm_Elem, gm_Vec),

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