namespace DistIL.IR;

public class UnaryInst : Instruction
{
    public UnaryOp Op { get; set; }

    public Value Value {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override string InstName => Op.ToString().ToLower();

    public UnaryInst(UnaryOp op, Value value)
        : base(value)
    {
        Op = op;
        ResultType = value.ResultType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public enum UnaryOp
{
    Neg, //int/long/nint
    Not, //int/long/nint
    FNeg //float/double
}