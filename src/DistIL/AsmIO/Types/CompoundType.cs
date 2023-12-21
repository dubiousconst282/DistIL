namespace DistIL.AsmIO;

/// <summary> Represents a type that has an element type (array, pointer, byref) </summary>
public abstract class CompoundType : TypeDesc
{
    public override TypeDesc ElemType { get; }
    public override TypeDesc? BaseType => ElemType.BaseType;
    public override bool IsUnboundGeneric => ElemType.IsUnboundGeneric;

    public override string? Namespace => ElemType.Namespace;
    public override string Name => ElemType.Name + Postfix;

    protected abstract string Postfix { get; }

    public CompoundType(TypeDesc elemType)
    {
        ElemType = elemType;
    }
    
    public override TypeDesc GetSpec(GenericContext context)
    {
        var specElemType = ElemType.GetSpec(context);
        return specElemType == ElemType ? this : New(specElemType);
    }
    protected abstract CompoundType New(TypeDesc elemType);

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        ElemType.Print(ctx, includeNs);
        ctx.Print(Postfix);
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ElemType);
    public override bool Equals(TypeDesc? other) 
        => other != null && other.GetType() == GetType() && ((CompoundType)other).ElemType == ElemType;
}