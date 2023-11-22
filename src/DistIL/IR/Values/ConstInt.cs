namespace DistIL.IR;

public class ConstInt : Const
{
    public long Value { get; }
    public ulong UValue => (ulong)(Value & GetMask(BitSize));

    public bool IsInt => ResultType.StackType == StackType.Int;
    public bool IsLong => ResultType.StackType == StackType.Long;

    public bool IsSigned => ResultType.Kind.IsSigned();
    public bool IsUnsigned => ResultType.Kind.IsUnsigned();
    public int BitSize => ResultType.Kind.BitSize();

    private ConstInt(TypeDesc type, long value)
    {
        Ensure.That(type.Kind.IsInt());
        ResultType = type;

        // truncate
        int size = BitSize;
        value &= GetMask(size);
        // sign extend
        if (IsSigned) {
            int shift = 64 - size;
            value = (value << shift) >> shift;
        }
        Value = value;
    }

    /// <summary> Checks whether this constant value fits in the specified type without being truncated. </summary>
    public bool FitsInType(TypeDesc type)
    {
        ulong mask = (ulong)GetMask(type.Kind.BitSize());

        if (type.Kind.IsSigned()) {
            return Value >= -(long)(mask / 2 + 1) && Value <= (long)(mask / 2);
        }
        return (UValue & ~mask) == 0;
    }

    private static long GetMask(int size) => size == 64 ? ~0L : (1L << size) - 1;

    static readonly ConstInt?[] s_SmallIntCache = new ConstInt?[8];

    public static ConstInt CreateI(int value)
    {
        if (value is >= -1 and < 7) {
            return s_SmallIntCache[value + 1] ??= new(PrimType.Int32, value);
        }
        return new(PrimType.Int32, value);
    }
    public static ConstInt CreateL(long value) => new(PrimType.Int64, value);

    public static ConstInt Create(TypeDesc type, long value) => new(type, value);

    public override void Print(PrintContext ctx)
    {
        ctx.Print(Value + (IsUnsigned ? "U" : "") + (IsLong ? "L" : ""), PrintToner.Number);
    }

    public override bool Equals(Const? other) => other is ConstInt o && o.Value.Equals(Value) && o.ResultType == ResultType;
    public override int GetHashCode() => Value.GetHashCode();
}