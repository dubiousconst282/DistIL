namespace DistIL.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;

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

    internal void Load2(ModuleLoader loader, GenericParameter info)
    {
        Constraints = DecodeGenericConstraints(loader, info.GetConstraints());
    }
    private static ImmutableArray<TypeDesc> DecodeGenericConstraints(ModuleLoader loader, GenericParameterConstraintHandleCollection handleList)
    {
        if (handleList.Count == 0) {
            return ImmutableArray<TypeDesc>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<TypeDesc>(handleList.Count);
        foreach (var handle in handleList) {
            var info = loader._reader.GetGenericParameterConstraint(handle);
            builder.Add((TypeDesc)loader.GetEntity(info.Type));
            //TODO: generic parameter constraint CAs
        }
        return builder.MoveToImmutable();
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

    public override int GetHashCode()
        => (Index * 2) + (IsMethodParam ? 1 : 0);
}