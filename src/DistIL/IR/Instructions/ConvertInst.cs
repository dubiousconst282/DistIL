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
        Ensure(dstType.Kind.IsFloat() ? !checkOverflow : true);

        ResultType = dstType;
        CheckOverflow = checkOverflow;
        SrcUnsigned = srcUnsigned;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(PrintContext ctx)
    {
        base.Print(ctx);
        ctx.Print(" -> ");
        ResultType.Print(ctx);
    }
}