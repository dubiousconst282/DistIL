namespace DistIL.Passes.Vectorization;

using DistIL.IR.Utils;

internal class VectorFuncTable
{
    readonly ModuleResolver _resolver;
    readonly Dictionary<(VectorType, string), MethodDesc> _funcTable = new();
    readonly Dictionary<VectorType, TypeSpec> _vecTypes = new();

    public VectorFuncTable(ModuleResolver resolver)
    {
        _resolver = resolver;
    }

    public Value BuildCall(IRBuilder builder, VectorType type, string funcName, params Value[] args)
    {
        var func = _funcTable.GetOrAddRef((type, funcName))
            ??= FindFunc(type, funcName);
        return builder.CreateCall(func, args);
    }

    private MethodDesc FindFunc(VectorType type, string name)
    {
        var baseType = GetBaseType(type);

        if (!name.Contains(':')) {
            Debug.Assert(baseType.Methods.Count(m => m.Name == name) == 1);

            var def = (MethodDef)baseType.FindMethod(name);
            return def.GetSpec(type.ElemType);
        }
        string actualName = name.Substring(0, name.IndexOf(':'));

        var vecType = GetActualType(type);
        var gt_Elem = new GenericParamType(0, isMethodParam: true);
        var gt_Vec = vecType.Definition.GetSpec(gt_Elem);

        return name switch {
            "Create:1"              => FindDef(vecType,         type.ElemType),
            "Create:N"              => FindDef(vecType,         Enumerable.Repeat((TypeSig)type.ElemType, type.Count).ToArray()),
            "LoadUnsafe:"           => FindGen(gt_Vec,          gt_Elem.CreateByref()),
            "StoreUnsafe:"          => FindGen(PrimType.Void,   gt_Vec, gt_Elem.CreateByref()),

            "Multiply:"             => FindGen(gt_Vec,          gt_Vec, gt_Vec),

            "ShiftLeft:"            => FindDef(vecType,         vecType, PrimType.Int32),
            "ShiftRightArithmetic:" => FindDef(vecType,         vecType, PrimType.Int32),
            "ShiftRightLogical:"    => FindDef(vecType,         vecType, PrimType.Int32),
            
            "Floor:"                => FindDef(vecType,         vecType),
            "Ceiling:"              => FindDef(vecType,         vecType),

            "Shuffle:"              => FindDef(vecType,         vecType, vecType.Definition.GetSpec(GetShuffleIndexType(type))),
        };
        MethodDesc FindDef(TypeDesc retType, params TypeSig[] parTypes)
            => baseType.FindMethod(actualName, new MethodSig(retType, parTypes));

        MethodDesc FindGen(TypeDesc retType, params TypeSig[] parTypes)
        {
            var sig = new MethodSig(retType, parTypes, numGenPars: 1);
            var def = (MethodDef)baseType.FindMethod(actualName, sig);
            return def.GetSpec(type.ElemType);
        }
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
}