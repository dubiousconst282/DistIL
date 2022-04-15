namespace DistIL.IR;

public class LoadVarInst : Instruction
{
    public Variable Source {
        get => (Variable)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override string InstName => "ldvar";

    public LoadVarInst(Variable src)
        : base(src)
    {
        ResultType = src.ResultType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StoreVarInst : Instruction
{
    public Variable Dest {
        get => (Variable)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Value {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public override string InstName => "stvar";
    public override bool HasSideEffects => true;

    public StoreVarInst(Variable dest, Value value)
        : base(dest, value)
    {
        Ensure(value.ResultType.IsStackAssignableTo(dest.ResultType));
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class VarAddrInst : Instruction
{
    public Variable Source {
        get => (Variable)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override string InstName => "varaddr";

    public VarAddrInst(Variable src)
        : base(src)
    {
        ResultType = new ByrefType(src.ResultType);
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}