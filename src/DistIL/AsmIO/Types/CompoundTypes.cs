namespace DistIL.AsmIO;

using System.Collections.Immutable;

using DistIL.IR;

/// <summary> Represents a type that has an element type (array, pointer, byref) </summary>
public abstract class CompoundType : TypeDesc
{
    public override TypeDesc ElemType { get; }
    public override TypeDesc? BaseType => ElemType.BaseType;

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
        if (specElemType == ElemType) {
            return this;
        }
        return New(specElemType);
    }
    protected abstract CompoundType New(TypeDesc elemType);

    public override void Print(PrintContext ctx, bool includeNs = true)
    {
        ElemType.Print(ctx, includeNs);
        ctx.Print(Postfix);
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ElemType);
    public override bool Equals(TypeDesc? other) 
        => other != null && other.GetType() == GetType() && ((CompoundType)other).ElemType == ElemType;
}

/// <summary> Represents an unmanaged pointer type. </summary>
public class PointerType : CompoundType
{
    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;
    public override TypeDesc? BaseType => null;
    protected override string Postfix => "*";

    public PointerType(TypeDesc elemType)
        : base(elemType)
    {
    }

    protected override CompoundType New(TypeDesc specElemType)
        => new PointerType(specElemType);
}

/// <summary> Represents a managed reference/pointer type. </summary>
public class ByrefType : CompoundType
{
    public override TypeKind Kind => TypeKind.ByRef;
    public override StackType StackType => StackType.ByRef;
    public override TypeDesc? BaseType => null;
    protected override string Postfix => "&";

    public ByrefType(TypeDesc elemType)
        : base(elemType)
    {
    }

    protected override CompoundType New(TypeDesc specElemType)
        => new ByrefType(specElemType);
}
/// <summary> Represents the type of a local variable that holds a pinned GC reference. </summary>
/// <remarks> This type should only ever be used to encode local variable types. </remarks>
public class PinnedType_ : CompoundType
{
    public override TypeKind Kind => ElemType.Kind;
    public override StackType StackType => ElemType.StackType;

    protected override string Postfix => "^";

    public PinnedType_(TypeDesc elemType)
        : base(elemType)
    {
    }

    protected override CompoundType New(TypeDesc specElemType)
        => throw new NotSupportedException();
}