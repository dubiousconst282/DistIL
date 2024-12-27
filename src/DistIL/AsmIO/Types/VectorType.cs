namespace DistIL.AsmIO;

/// <summary> Pseudo-type representing a fixed-size SIMD vector of arbitrary values. </summary>
/// <remarks> This is not supported by the CIL backend, all uses must be lowered into concrete types before codegen. </remarks>
public class VectorType : CompoundType
{
    public override TypeKind Kind => TypeKind.Struct;
    public override StackType StackType => StackType.Struct;

    protected override string Postfix => "[x" + Width + "]";
    public int Width { get; }

    private VectorType(TypeDesc elemType, int width) : base(elemType)
    {
        Width = width;
    }

    protected override CompoundType New(TypeDesc elemType)
    {
        return Create(elemType, Width);
    }

    public static VectorType Create(TypeDesc elemType, int width)
    {
        // TODO: consider caching vector types
        return new VectorType(elemType, width);
    }

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        ElemType.Print(ctx, includeNs);
        ctx.Print(Postfix, PrintToner.Number);
    }

    public override bool Equals(TypeDesc? other) => other is VectorType vec && vec.ElemType == ElemType && vec.Width == Width;
    public override int GetHashCode() => HashCode.Combine(ElemType, GetType().GetHashCode() + Width);
}
