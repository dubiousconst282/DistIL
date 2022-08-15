namespace DistIL.AsmIO;

using System.Reflection;

using DistIL.IR;

/// <summary> Represents a placeholder for a generic type argument. </summary>
public class GenericParamType : TypeDesc
{
    public override TypeKind Kind => TypeKind.Object;
    public override StackType StackType => StackType.Object;

    public override TypeDesc? BaseType => null;
    public override string? Namespace => null;
    public override string Name { get; }

    public ImmutableArray<TypeDesc> Constraints { get; }
    public GenericParameterAttributes Attribs { get; }

    public int Index { get; }
    public bool IsMethodParam { get; }

    public GenericParamType(int index, bool isMethodParam, string? name = null, ImmutableArray<TypeDesc> constraints = default)
    {
        Index = index;
        IsMethodParam = isMethodParam;
        Name = name ?? Index.ToString();
        if (!constraints.IsDefault) {
            Constraints = constraints;
        }
    }

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        ctx.Print(IsMethodParam ? "!!" : "!");
        base.Print(ctx, includeNs);
    }

    public override TypeDesc GetSpec(GenericContext context)
    {
        var args = IsMethodParam ? context.MethodArgs : context.TypeArgs;
        return Index < args.Length ? args[Index] : this;
    }

    public override bool Equals(TypeDesc? other)
        => other is GenericParamType o && o.Index == Index && o.IsMethodParam == IsMethodParam;
}