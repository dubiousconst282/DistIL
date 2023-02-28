namespace DistIL.Passes.Vectorization;

using System.Numerics;

internal struct VectorType : IEquatable<VectorType>
{
    public const int MaxBitWidth = 256, MinBitWidth = 128;

    public TypeKind ElemKind { get; }
    public int Count { get; }

    public int BitWidth => ElemKind.BitSize() * Count;
    public TypeDesc ElemType => PrimType.GetFromKind(ElemKind);

    public bool IsEmpty => Count == 0;

    private VectorType(TypeKind elemKind, int count)
    {
        ElemKind = elemKind;
        Count = count;
    }

    public static VectorType Create(TypeDesc elemType, int elemCount, bool throwIfNotSupported = false)
    {
        if (!IsSupportedElemType(elemType) || !IsSupportedWidth(elemCount * elemType.Kind.BitSize())) {
            Ensure.That(!throwIfNotSupported);
            return default;
        }
        return new VectorType(elemType.Kind, elemCount);
    }

    public static VectorType CreateUnsafe(TypeKind elemKind, int elemCount)
    {
        return new VectorType(elemKind, elemCount);
    }

    public static VectorType GetBiggest(TypeDesc elemType, int maxElemCount = 0)
    {
        if (!IsSupportedElemType(elemType)) {
            return default;
        }
        int elemSize = elemType.Kind.BitSize();

        for (int width = MaxBitWidth; width >= MinBitWidth; width /= 2) {
            if (width / elemSize <= maxElemCount) {
                return new VectorType(elemType.Kind, width / elemSize);
            }
        }
        return default;
    }
    public static bool IsSupportedElemType(TypeDesc elemType)
    {
        return elemType.Kind is >= TypeKind.SByte and <= TypeKind.Double;
    }
    public static bool IsSupportedWidth(int bitWidth)
    {
        return BitOperations.IsPow2(bitWidth) && bitWidth is >= MinBitWidth and <= MaxBitWidth;
    }

    public bool Equals(VectorType other) => other.Count == Count && other.ElemKind == ElemKind;
    public override bool Equals(object? obj) => obj is VectorType vt && Equals(vt);
    public override int GetHashCode() => Count * 10000 + (int)ElemKind;

    public override string ToString() => $"Vector{BitWidth}<{ElemKind}>";
}