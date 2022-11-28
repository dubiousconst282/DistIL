namespace DistIL.IR;

public abstract class VarAccessInst : Instruction, AccessInst
{
    public Variable Var {
        get => (Variable)Operands[0];
        set => ReplaceOperand(0, value);
    }

    Value AccessInst.Location => Var;

    public VarAccessInst(TypeDesc resultType, params Value[] operands)
        : base(operands)
    {
        ResultType = resultType;
    }
}
public class LoadVarInst : VarAccessInst
{
    public override string InstName => "ldvar";

    public LoadVarInst(Variable src)
        : base(src.ResultType, src) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StoreVarInst : VarAccessInst, StoreInst
{
    public Value Value {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }

    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;
    public override string InstName => "stvar";

    public bool IsCoerced => StoreInst.IsCoerced(Var.ResultType, Value.ResultType);

    public StoreVarInst(Variable dest, Value value)
        : base(PrimType.Void, dest, value) {  }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class VarAddrInst : VarAccessInst
{
    public override string InstName => "varaddr";

    public VarAddrInst(Variable src)
        : base(src.ResultType.CreateByref(), src) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}