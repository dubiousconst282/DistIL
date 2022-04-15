namespace DistIL.IR;

/// <summary> Represents a type that is made up by another (array, pointer, byref) </summary>
public abstract class CompoundType : RType
{
    public override RType ElemType { get; }
    public override string? Namespace => ElemType.Namespace;
    public override string Name => ElemType.Name + Postfix;

    protected abstract string Postfix { get; }

    public CompoundType(RType elemType)
    {
        ElemType = elemType;
    }

    public override void Print(StringBuilder sb)
    {
        ElemType.Print(sb);
        sb.Append(Postfix);
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ElemType);
}

/// <summary> Represents an unmanaged pointer type. </summary>
public class PointerType : CompoundType
{
    public override TypeKind Kind => TypeKind.Pointer;
    public override StackType StackType => StackType.NInt;

    protected override string Postfix => "*";

    public PointerType(RType elemType)
        : base(elemType)
    {
    }

    public override bool Equals(RType? other)
        => other is PointerType o && o.ElemType == ElemType;
}

/// <summary> Represents a managed reference/pointer type. </summary>
public class ByrefType : CompoundType
{
    public override TypeKind Kind => TypeKind.ByRef;
    public override StackType StackType => StackType.ByRef;

    protected override string Postfix => "&";

    public ByrefType(RType elemType)
        : base(elemType)
    {
    }

    public override bool Equals(RType? other)
        => other is ByrefType o && o.ElemType == ElemType;
}

/// <summary> Represents a single dimensional array type. </summary>
public class ArrayType : CompoundType
{
    public override TypeKind Kind => TypeKind.Array;
    public override StackType StackType => StackType.Object;

    protected override string Postfix => "[]";

    public ArrayType(RType elemType)
        : base(elemType)
    {
    }

    public override bool Equals(RType? other)
        => other is ArrayType o && o.ElemType == ElemType;
}
/// <summary> Represents an unnecessarily complicated multi-dimensional array type. </summary>
public class MDArrayType : CompoundType
{
    public override TypeKind Kind => TypeKind.Array;
    public override StackType StackType => StackType.Object;

    public int Rank { get; }
    public ImmutableArray<int> LowerBounds { get; }
    public ImmutableArray<int> Sizes { get; }

    protected override string Postfix {
        get {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < Rank; i++) {
                if (i != 0) sb.Append(',');

                int lowerBound = 0;

                if (i < LowerBounds.Length) {
                    lowerBound = LowerBounds[i];
                    sb.Append(lowerBound);
                }
                sb.Append("...");

                if (i < Sizes.Length) {
                    sb.Append(lowerBound + Sizes[i] - 1);
                }
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    public MDArrayType(RType elemType, int rank, ImmutableArray<int> lowerBounds, ImmutableArray<int> sizes)
        : base(elemType)
    {
        Rank = rank;
        LowerBounds = lowerBounds;
        Sizes = sizes;
    }

    public override bool Equals(RType? other)
        => other is MDArrayType o && o.ElemType == ElemType && o.Rank == Rank && 
           o.Sizes.SequenceEqual(Sizes) && o.LowerBounds.SequenceEqual(LowerBounds);
}