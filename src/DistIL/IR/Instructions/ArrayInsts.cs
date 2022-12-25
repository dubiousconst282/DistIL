namespace DistIL.IR;

//Maybe IntrinsicId.ArrayLen?
public class ArrayLenInst : Instruction
{
    public Value Array {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public override string InstName => "arrlen";
    public override bool MayThrow => true;

    public ArrayLenInst(Value array)
        : base(array)
    {
        ResultType = PrimType.IntPtr;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public abstract class ArrayAccessInst : Instruction, AccessInst
{
    public override bool MayThrow => true;

    public Value Array {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Index {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public abstract TypeDesc ElemType { get; }
    public ArrayAccessFlags Flags { get; set; }

    Value AccessInst.Location => Array;
    TypeDesc AccessInst.LocationType => ElemType;

    protected ArrayAccessInst(ArrayAccessFlags flags, params Value[] operands)
        : base(operands)
    {
        Flags = flags;
    }
}

public class LoadArrayInst : ArrayAccessInst, LoadInst
{
    public override TypeDesc ElemType => ResultType;
    public override string InstName => "ldarr";

    public LoadArrayInst(Value array, Value index, TypeDesc elemType, ArrayAccessFlags flags = 0)
        : base(flags, array, index)
    {
        ResultType = elemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StoreArrayInst : ArrayAccessInst, StoreInst
{
    public Value Value {
        get => Operands[2];
        set => ReplaceOperand(2, value);
    }
    public override TypeDesc ElemType { get; }

    public override string InstName => "starr";
    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;

    public StoreArrayInst(Value array, Value index, Value value, TypeDesc elemType, ArrayAccessFlags flags = 0)
        : base(flags, array, index, value)
    {
        ElemType = elemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
    
    protected override void PrintOperands(PrintContext ctx)
    {
        base.PrintOperands(ctx);
        ctx.Print(" as ", PrintToner.InstName);
        ElemType.Print(ctx);
    }
}
public class ArrayAddrInst : ArrayAccessInst
{
    /// <summary> Specifies the access type. For primitive arrays, it is used as the element stride (address = baseAddr + index * elemStride). </summary>
    public override TypeDesc ElemType => ResultType.ElemType!;

    public override string InstName => "arraddr";

    public ArrayAddrInst(Value array, Value index, TypeDesc elemType, ArrayAccessFlags flags = 0)
        : base(flags, array, index)
    {
        ResultType = elemType.CreateByref();
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public enum ArrayAccessFlags
{
    None            = 0,
    NoBoundsCheck   = 1 << 0,
    NoTypeCheck     = 1 << 1,
    NoNullCheck     = 1 << 2,
    ReadOnly        = 1 << 4,
}