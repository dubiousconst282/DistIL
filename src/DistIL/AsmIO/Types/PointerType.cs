namespace DistIL.AsmIO;

/// <summary> Represents an unmanaged pointer type. </summary>
public class PointerType : CompoundType
{
    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;
    public override TypeDesc? BaseType => null;
    protected override string Postfix => "*";

    internal PointerType(TypeDesc elemType)
        : base(elemType) { }

    protected override CompoundType New(TypeDesc specElemType)
        => new PointerType(specElemType);
}

/// <summary> Represents a managed reference/pointer type. </summary>
public class ByrefType : PointerType
{
    public override TypeKind Kind => TypeKind.ByRef;
    public override StackType StackType => StackType.ByRef;
    public override TypeDesc? BaseType => null;
    protected override string Postfix => "&";

    internal ByrefType(TypeDesc elemType)
        : base(elemType) { }

    protected override CompoundType New(TypeDesc specElemType)
        => new ByrefType(specElemType);
}