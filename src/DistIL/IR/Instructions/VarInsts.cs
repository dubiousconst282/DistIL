namespace DistIL.IR;

public abstract class VarAccessInst : Instruction
{
    public Variable Var {
        get => (Variable)Operands[0];
        set => ReplaceOperand(0, value);
    }

    public VarAccessInst(params Value[] operands)
        : base(operands)
    {
    }
}
public class LoadVarInst : VarAccessInst
{
    public override string InstName => "ldvar";

    public LoadVarInst(Variable src)
        : base(src)
    {
        ResultType = src.ResultType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StoreVarInst : VarAccessInst
{
    public Value Value {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }

    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;
    public override string InstName => "stvar";

    public StoreVarInst(Variable dest, Value value)
        : base(dest, value)
    {
        Ensure(value.ResultType.IsStackAssignableTo(dest.ResultType));
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class VarAddrInst : VarAccessInst
{
    public override string InstName => "varaddr";

    public VarAddrInst(Variable src)
        : base(src)
    {
        ResultType = new ByrefType(src.ResultType);
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}