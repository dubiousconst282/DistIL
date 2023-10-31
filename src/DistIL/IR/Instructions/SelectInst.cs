namespace DistIL.IR;

/// <summary> Conditionally selects one of two values, i.e: <c>Cond ? IfTrue : IfFalse</c> </summary>
public class SelectInst : Instruction
{
    public Value Cond {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value IfTrue {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public Value IfFalse {
        get => Operands[2];
        set => ReplaceOperand(2, value);
    }
    public override string InstName => "select";

    public SelectInst(Value cond, Value ifTrue, Value ifFalse, TypeDesc resultType)
        : base(cond, ifTrue, ifFalse)
    {
        ResultType = resultType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print($" {Cond} ? {IfTrue} : {IfFalse}");
    }
}