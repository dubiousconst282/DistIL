namespace DistIL.IR;

/// <summary> Convert numeric value type. (sign extend, zero extend, truncate, float<->int) </summary>
public class ConvertInst : Instruction
{
    public Value Value {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public bool CheckOverflow { get; set; }
    /// <summary> Treat the source value as unsigned. Only relevant if target type is float or `CheckOverflow == true`. </summary>
    public bool SrcUnsigned { get; set; }

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

    protected override void PrintOperands(PrintContext ctx)
    {
        base.PrintOperands(ctx);
        ctx.Print($"{PrintToner.Comment} /* {Value.ResultType}{PrintToner.Comment} -> {ResultType}{PrintToner.Comment} */");
    }
}