namespace DistIL.AsmIO;

using System.Text;

using DistIL.IR;

/// <summary> Represents a generic type instantiation. </summary>
public class TypeSpec : RType
{
    /// <summary> The generic type definition. </summary>
    public TypeDef GenericType { get; }
    public ImmutableArray<RType> GenericArgs { get; }

    public override TypeKind Kind => GenericType.Kind;
    public override StackType StackType => GenericType.StackType;

    public override string? Namespace => GenericType.Namespace;
    public override string Name => GenericType.Name;

    public TypeSpec(TypeDef genType, ImmutableArray<RType> args)
    {
        GenericType = genType;
        GenericArgs = args;
    }

    public override void Print(StringBuilder sb)
    {
        base.Print(sb);
        sb.Append("[");
        sb.AppendJoin(", ", GenericArgs);
        sb.Append("]");
    }

    public override bool Equals(RType? other)
        => other is TypeSpec o && o.GenericType == GenericType &&
           o.GenericArgs.SequenceEqual(GenericArgs);
}

/// <summary> Represents a placeholder type for a generic parameters. </summary>
public class PlaceholderType : RType
{
    public override TypeKind Kind => TypeKind.Void;
    public override StackType StackType => StackType.Void;

    public override string? Namespace => null;
    public override string Name => (IsMethodParam ? "!!" : "!") + Index;

    public int Index { get; }
    public bool IsMethodParam { get; }

    public PlaceholderType(int index, bool isMethodParam)
    {
        Index = index;
        IsMethodParam = isMethodParam;
    }

    public override bool Equals(RType? other)
        => other is PlaceholderType o && o.Index == Index && o.IsMethodParam == IsMethodParam;
}

public struct GenericContext
{
    static PlaceholderType?[] s_placeholderTypeArgCache = { };
    static PlaceholderType?[] s_placeholderMethodArgCache = { };

    public ImmutableArray<RType> TypeArgs { get; }
    public ImmutableArray<RType> MethodArgs { get; }

    public bool IsDefault => TypeArgs.IsDefault && MethodArgs.IsDefault;

    public RType GetTypeArg(int index) => TypeArgs.IsDefault ? GetPlaceholder(index, false) : TypeArgs[index];
    public RType GetMethodArg(int index) => MethodArgs.IsDefault ? GetPlaceholder(index, true) : MethodArgs[index];

    private RType GetPlaceholder(int index, bool isMethod)
    {
        ref var cache = ref (isMethod ? ref s_placeholderMethodArgCache : ref s_placeholderTypeArgCache); //cursed_ref_ternary
        if (index >= cache.Length) {
            Array.Resize(ref cache, Math.Max(cache.Length * 2, index + 4));
        }
        return cache[index] ??= new PlaceholderType(index, isMethod);
    }
}
