namespace DistIL.IR;

public class ConstInt : Const
{
    private long _value;

    public long Value {
        get => _value;
        set {
            //truncate
            int size = BitSize;
            long mask = size == 64 ? ~0L : (1L << size) - 1;
            value &= mask;
            //sign extend
            if (IsSigned) {
                int shift = 64 - size;
                value = (value << shift) >> shift;
            }
            _value = value;
        }
    }
    public ulong UValue {
        get {
            //truncate
            int size = BitSize;
            long mask = size == 64 ? ~0L : (1L << size) - 1;
            return (ulong)(_value & mask);
        }
        set => Value = (long)value;
    }

    public bool IsInt => ResultType.StackType == StackType.Int;
    public bool IsLong => ResultType.StackType == StackType.Long;

    public bool IsSigned => ResultType.Kind.IsSigned();
    public bool IsUnsigned => !ResultType.Kind.IsSigned();
    public int BitSize => ResultType.Kind.BitSize();

    private ConstInt() { }

    public static ConstInt CreateI(int value) => Create(PrimType.Int32, value);
    public static ConstInt CreateL(long value) => Create(PrimType.Int64, value);

    public static ConstInt Create(TypeDesc type, long value)
    {
        Ensure(type.Kind.IsInt());
        return new() { ResultType = type, Value = value };
    }

    /// <summary> Changes the type of the integer value, and normalize the value to it. </summary>
    public void SetType(PrimType dstType)
    {
        Ensure(dstType.Kind.IsInt());
        ResultType = dstType;
        Value = _value; //setter will normalize value
    }

    public override void Print(PrintContext ctx)
    {
        ctx.Print(Value + (IsUnsigned ? "U" : "") + (IsLong ? "L" : ""), PrintToner.Number);
    }

    public override bool Equals(Const? other) => other is ConstInt o && o.Value.Equals(Value) && o.ResultType == ResultType;
    public override int GetHashCode() => Value.GetHashCode();
}