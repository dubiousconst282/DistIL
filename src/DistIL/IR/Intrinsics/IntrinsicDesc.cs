namespace DistIL.IR.Intrinsics;

using System.Collections;

public abstract class IntrinsicDesc
{
    public abstract string Namespace { get; }
    public abstract string Name { get; }
    public ImmutableArray<TypeDesc> ParamTypes { get; protected init; } = ImmutableArray<TypeDesc>.Empty;
    public TypeDesc ReturnType { get; protected init; } = PrimType.Void;

    public virtual TypeDesc GetResultType(Value[] args)
    {
        return ResolveType(ReturnType, args);
    }

    public virtual bool IsAcceptableArgument(Value[] args, int index)
    {
        var parType = ParamTypes[index];

        return parType == s_AnyType ? true :
               parType == s_TypePar ? args[index] is TypeDesc :
               args[index].ResultType.IsStackAssignableTo(ResolveType(parType, args));
    }

    public static TypeDesc ResolveType(TypeDesc type, Value[] args)
    {
        //Avoid allocating proxy list if type is complete
        if (type is PrimType or TypeDefOrSpec) {
            return type;
        }
        var ctx = new GenericContext(methodArgs: new ValueTypeProxyList() { Values = args });
        return type.GetSpec(ctx);
    }

    public override string ToString()
    {
        return $"{ReturnType} {Namespace}::{Name}({string.Join(", ", ParamTypes)})";
    }

    protected static readonly TypeDesc
        s_TypePar = new GenericParamType(0, false, "$Type"),
        s_AnyType = new GenericParamType(-1, false, "Any"),
        s_Typeof0 = new GenericParamType(0, true, "TypeofArg0"),
        s_Typeof1 = new GenericParamType(1, true, "TypeofArg1");

    private static TypeDesc GetActualType(Value value) => value as TypeDesc ?? value.ResultType;

    private class ValueTypeProxyList : IReadOnlyList<TypeDesc>
    {
        public Value[] Values = null!;

        public TypeDesc this[int index] => GetActualType(Values[index]);
        public int Count => Values.Length;

        public IEnumerator<TypeDesc> GetEnumerator() => Values.Select(GetActualType).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}


public static class IntrinsicExt
{
    public static bool Is(this Instruction inst, CilIntrinsicId id) 
        => inst is IntrinsicInst { Intrinsic: CilIntrinsic c } && c.Id == id;

    public static bool Is(this Instruction inst, IRIntrinsicId id)
        => inst is IntrinsicInst { Intrinsic: IRIntrinsic c } && c.Id == id;
}