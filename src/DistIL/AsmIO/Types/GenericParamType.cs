namespace DistIL.AsmIO;

using System.Reflection;

/// <summary> Represents a placeholder for a generic type argument. </summary>
public class GenericParamType : TypeDesc
{
    public override TypeKind Kind => TypeKind.Object;
    public override StackType StackType => StackType.Object;

    public override TypeDesc? BaseType => null;
    public override string? Namespace => null;
    public override string Name { get; }

    public GenericParameterAttributes Attribs { get; }

    public int Index { get; }
    public bool IsMethodParam { get; }

    // TODO: proper immutability for these (or at least when sharing unbound instances)
    public ImmutableArray<GenericParamConstraint> Constraints { get; set; }
    public IList<CustomAttrib> CustomAttribs { get; set; } = Array.Empty<CustomAttrib>();

    public bool IsCovariant => (Attribs & GenericParameterAttributes.Covariant) != 0;
    public bool IsContravariant => (Attribs & GenericParameterAttributes.Contravariant) != 0;

    public GenericParamType(
        int index, bool isMethodParam, string? name = null,
        GenericParameterAttributes attribs = 0,
        ImmutableArray<GenericParamConstraint> constraints = default)
    {
        Index = index;
        IsMethodParam = isMethodParam;
        Name = name ?? Index.ToString();
        Attribs = attribs;
        Constraints = constraints.EmptyIfDefault();
    }

    const int kCacheSize = 16;
    static readonly GenericParamType?[] s_UnboundCache = new GenericParamType?[kCacheSize * 2];

    /// <summary> Returns an unbound generic type parameter at <paramref name="index"/>. </summary>
    public static TypeDesc GetUnboundT(int index) => GetUnbound(index, isMethodParam: false);

    /// <summary> Returns an unbound generic method parameter at <paramref name="index"/>. </summary>
    public static TypeDesc GetUnboundM(int index) => GetUnbound(index, isMethodParam: true);

    public static TypeDesc GetUnbound(int index, bool isMethodParam)
    {
        if (index >= 0 && index < kCacheSize) {
            int cacheIdx = index + (isMethodParam ? kCacheSize : 0);
            return s_UnboundCache[cacheIdx] ??= new(index, isMethodParam);
        }
        return new GenericParamType(index, isMethodParam);
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        ctx.Print(IsMethodParam ? "!!" : "!");
        base.Print(ctx, includeNs);
    }

    public override TypeDesc GetSpec(GenericContext context)
        => context.GetArgument(Index, IsMethodParam) ?? this;

    public override bool Equals(TypeDesc? other)
        => other is GenericParamType o && o.Index == Index && o.IsMethodParam == IsMethodParam;

    public override int GetHashCode()
        => (Index * 2) + (IsMethodParam ? 1 : 0);
}
public readonly record struct GenericParamConstraint(TypeSig Sig, IList<CustomAttrib> CustomAttribs);

// public enum GenericParamOwner { Type, Method }