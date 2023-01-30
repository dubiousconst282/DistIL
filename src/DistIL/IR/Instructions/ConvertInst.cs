namespace DistIL.IR;

/// <summary> Converts a numeric value. (sign extend, zero extend, truncate, float&lt;->int) </summary>
public class ConvertInst : Instruction
{
    public Value Value {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public bool CheckOverflow { get; set; }
    /// <summary> Treat the source value as unsigned. Only relevant if target type is float or <c>CheckOverflow == true</c>. </summary>
    public bool SrcUnsigned { get; set; }

    public TypeDesc SrcType => Value.ResultType;

    public bool IsExtension => IsSizeDiffDir(SrcType.Kind, ResultType.Kind, +1);
    public bool IsTruncation => IsSizeDiffDir(SrcType.Kind, ResultType.Kind, -1);

    public bool IsSignExtension => IsExtension && ResultType.Kind.IsSigned();
    public bool IsZeroExtension => IsExtension && ResultType.Kind.IsUnsigned();

    public override bool MayThrow => CheckOverflow;
    public override string InstName => "conv" + (CheckOverflow ? ".ovf" : "") + (SrcUnsigned ? ".un" : "");

    public ConvertInst(Value srcValue, TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
        : base(srcValue)
    {
        Ensure.That(dstType.StackType is >= StackType.Int and <= StackType.Float, "Can only convert to a primitive type");
        Ensure.That(!checkOverflow || dstType.StackType != StackType.Float, "Cannot check overflow for floating-point types");

        ResultType = dstType;
        CheckOverflow = checkOverflow;
        SrcUnsigned = srcUnsigned;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    private static bool IsSizeDiffDir(TypeKind srcType, TypeKind dstType, int sign)
    {
        //Assume that pointer size is at least 32 bits
        int srcSize = srcType.IsPointerSize() ? 32 : srcType.BitSize();
        int dstSize = dstType.IsPointerSize() ? 32 : dstType.BitSize();

        return Math.Sign(dstSize - srcSize) == sign;
    }
}