namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

using DistIL.IR;

/// <summary> Represents a placeholder for a generic type argument. </summary>
public class GenericParamType : TypeDesc
{
    public override TypeKind Kind => TypeKind.Object;
    public override StackType StackType => StackType.Object;

    public override TypeDesc? BaseType => null;
    public override string? Namespace => null;
    public override string Name { get; }

    public ImmutableArray<TypeDesc> Constraints { get; private set; }
    public GenericParameterAttributes Attribs { get; }

    public int Index { get; }
    public bool IsMethodParam { get; }

    public GenericParamType(int index, bool isMethodParam, string? name = null, GenericParameterAttributes attribs = 0, ImmutableArray<TypeDesc> constraints = default)
    {
        Index = index;
        IsMethodParam = isMethodParam;
        Name = name ?? Index.ToString();
        Attribs = attribs;
        Constraints = constraints.EmptyIfDefault();
    }

    internal void Load(ModuleLoader loader, GenericParameter info)
    {
        Constraints = loader.DecodeGenericConstraints(info.GetConstraints());
    }

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        ctx.Print(IsMethodParam ? "!!" : "!");
        base.Print(ctx, includeNs);
    }

    public override TypeDesc GetSpec(GenericContext context)
        => context.GetArgument(Index, IsMethodParam) ?? this;

    public override bool Equals(TypeDesc? other)
        => other is GenericParamType o && o.Index == Index && o.IsMethodParam == IsMethodParam;
}