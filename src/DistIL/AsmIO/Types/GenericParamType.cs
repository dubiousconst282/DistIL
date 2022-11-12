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

    public ImmutableArray<TypeSig> Constraints { get; private set; }
    public GenericParameterAttributes Attribs { get; }

    internal CustomAttrib[]? _customAttribs;
    internal CustomAttrib[]?[]? _constraintCustomAttribs;

    public int Index { get; }
    public bool IsMethodParam { get; }

    public GenericParamType(int index, bool isMethodParam, string? name = null, GenericParameterAttributes attribs = 0, ImmutableArray<TypeSig> constraints = default)
    {
        Index = index;
        IsMethodParam = isMethodParam;
        Name = name ?? Index.ToString();
        Attribs = attribs;
        Constraints = constraints.EmptyIfDefault();
    }

    internal void Load3(ModuleLoader loader, GenericParameter info)
    {
        Constraints = DecodeGenericConstraints(loader, info.GetConstraints());

        var customAttribHandles = info.GetCustomAttributes();
        if (customAttribHandles.Count > 0) {
            _customAttribs = loader.DecodeCustomAttribs(customAttribHandles);
        }
    }
    private ImmutableArray<TypeSig> DecodeGenericConstraints(ModuleLoader loader, GenericParameterConstraintHandleCollection handleList)
    {
        if (handleList.Count == 0) {
            return ImmutableArray<TypeSig>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<TypeSig>(handleList.Count);
        foreach (var handle in handleList) {
            var info = loader._reader.GetGenericParameterConstraint(handle);
            var constraint = loader.GetEntity(info.Type);
            builder.Add(constraint as TypeDesc ?? ((ModifiedTypeSpecTableWrapper_)constraint).Sig);

            var customAttribHandles = info.GetCustomAttributes();
            if (customAttribHandles.Count > 0) {
                Array.Resize(ref _constraintCustomAttribs, builder.Count);
                _constraintCustomAttribs[builder.Count - 1] = loader.DecodeCustomAttribs(customAttribHandles);
            }
        }
        return builder.MoveToImmutable();
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