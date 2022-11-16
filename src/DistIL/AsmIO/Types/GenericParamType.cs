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

    internal CustomAttrib[]?[]? _customAttribs; //[0]: own, [1..]: constraints

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
        Constraints = DecodeConstraints(loader, info.GetConstraints());
        AddCustomAttribs(0, loader.DecodeCustomAttribs(info.GetCustomAttributes()));
    }
    private ImmutableArray<TypeSig> DecodeConstraints(ModuleLoader loader, GenericParameterConstraintHandleCollection handles)
    {
        if (handles.Count == 0) {
            return ImmutableArray<TypeSig>.Empty;
        }
        var builder = ImmutableArray.CreateBuilder<TypeSig>(handles.Count);
        foreach (var handle in handles) {
            var info = loader._reader.GetGenericParameterConstraint(handle);
            var constraint = loader.GetEntity(info.Type);
            builder.Add(constraint as TypeDesc ?? ((ModifiedTypeSpecTableWrapper_)constraint).Sig);
            AddCustomAttribs(builder.Count, loader.DecodeCustomAttribs(info.GetCustomAttributes()));
        }
        return builder.MoveToImmutable();
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
    {
        Ensure.That(readOnly, "Not impl");
        
        return _customAttribs?[0] ?? Array.Empty<CustomAttrib>();
    }

    public IList<CustomAttrib> GetCustomAttribs(TypeSig constraint, bool readOnly = true)
    {
        Ensure.That(readOnly, "Not impl");

        int index = Constraints.IndexOf(constraint);
        Ensure.That(index >= 0, "Generic parameter is not constrainted by the specified type");
        return _customAttribs?.ElementAtOrDefault(index + 1) ?? Array.Empty<CustomAttrib>();
    }

    private void AddCustomAttribs(int index, CustomAttrib[]? attribs)
    {
        if (attribs != null) {
            Array.Resize(ref _customAttribs, index + 1);
            _customAttribs[index] = attribs;
        }
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