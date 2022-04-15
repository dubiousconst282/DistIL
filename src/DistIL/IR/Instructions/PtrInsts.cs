namespace DistIL.IR;

public abstract class PtrAccessInst : Instruction
{
    public Value Address {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override bool MayThrow => true;

    public bool Unaligned { get; set; }
    public bool Volatile { get; set; }

    protected PtrAccessInst(bool un, bool vlt, params Value[] operands)
        : base(operands)
    {
        Unaligned = un;
        Volatile = vlt;
    }
}
public class LoadPtrInst : PtrAccessInst
{
    public RType ElemType {
        get => ResultType;
        set => ResultType = value;
    }
    public override string InstName => "ldptr" + (Unaligned ? ".un" : "") + (Volatile ? ".volatile" : "");

    public LoadPtrInst(Value addr, RType elemType, bool isUnaligned = false, bool isVolatile = false)
        : base(isUnaligned, isVolatile, addr)
    {
        ElemType = elemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StorePtrInst : PtrAccessInst
{
    public Value Value {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public RType ElemType { get; set; }

    public override string InstName => "stptr" + (Unaligned ? ".un" : "") + (Volatile ? ".volatile" : "");
    public override bool HasSideEffects => true;

    public StorePtrInst(Value addr, Value value, RType elemType, bool isUnaligned = false, bool isVolatile = false)
        : base(isUnaligned, isVolatile, addr, value)
    {
        ElemType = elemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}