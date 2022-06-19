namespace DistIL.IR;

public abstract class PtrAccessInst : Instruction
{
    public Value Address {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override bool MayThrow => true;

    public abstract TypeDesc ElemType { get; set; }
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
    public override TypeDesc ElemType {
        get => ResultType;
        set => ResultType = value;
    }
    public override string InstName => "ldptr" + (Unaligned ? ".un" : "") + (Volatile ? ".volatile" : "");

    public LoadPtrInst(Value addr, TypeDesc elemType, PointerFlags flags = PointerFlags.None)
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
    public override TypeDesc ElemType { get; set; }

    public override string InstName => "stptr" + (Unaligned ? ".un" : "") + (Volatile ? ".volatile" : "");
    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;

    public StorePtrInst(Value addr, Value value, TypeDesc elemType, PointerFlags flags = PointerFlags.Volatile)
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