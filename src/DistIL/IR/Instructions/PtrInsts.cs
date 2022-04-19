namespace DistIL.IR;

public abstract class PtrAccessInst : Instruction
{
    public Value Address {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override bool MayThrow => true;

    public abstract RType ElemType { get; set; }
    public PointerFlags Flags { get; set; }

    public bool Unaligned => (Flags & PointerFlags.Unaligned) != 0;
    public bool Volatile => (Flags & PointerFlags.Volatile) != 0;

    protected PtrAccessInst(PointerFlags flags, params Value[] operands)
        : base(operands)
    {
        Flags = flags;
    }
}
public class LoadPtrInst : PtrAccessInst
{
    public override RType ElemType {
        get => ResultType;
        set => ResultType = value;
    }
    public override string InstName => "ldptr" + (Unaligned ? ".un" : "") + (Volatile ? ".volatile" : "");

    public LoadPtrInst(Value addr, RType elemType, PointerFlags flags = PointerFlags.None)
        : base(flags, addr)
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
    public override RType ElemType { get; set; }

    public override string InstName => "stptr" + (Unaligned ? ".un" : "") + (Volatile ? ".volatile" : "");
    public override bool HasSideEffects => true;

    public StorePtrInst(Value addr, Value value, RType elemType, PointerFlags flags = PointerFlags.Volatile)
        : base(flags, addr, value)
    {
        ElemType = elemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public enum PointerFlags
{
    None = 0,
    Unaligned   = 1 << 0,
    Volatile    = 1 << 1
}