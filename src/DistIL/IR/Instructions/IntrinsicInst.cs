namespace DistIL.IR;

public class IntrinsicInst : Instruction
{
    public IntrinsicId Id { get; set; }
    public ReadOnlySpan<Value> Args => Operands.AsSpan();

    public override bool HasSideEffects => true;
    public override bool MayThrow => true;
    public override string InstName => "intrin." + Id.ToString();

    public IntrinsicInst(IntrinsicId intrinsic, TypeDesc resultType, params Value[] args)
        : base(args)
    {
        Id = intrinsic;
        ResultType = resultType;
    }

    public Value GetArg(int index) => Operands[index];
    public void SetArg(int index, Value newValue) => ReplaceOperand(index, newValue);

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public enum IntrinsicId
{
    None,           //not a real intrinsic

    NewArray,       //T[] newarr<T[]>(int|nint length)

    CheckFinite,    //float ckfinite(float), throw if x is NaN or +-Infinity
    MemCopy,        //void cpblk(void*|void& dst, void*|void& src, uint len)
    MemSet,         //void initblk(void*|void& dst, byte val, uint len)

    CopyObj,        //void cpobj<T>(T*|T& dst, T*|T& src)
    InitObj,        //void initobj<T>(T*|T& dst)

    SizeOf,         //uint sizeof<T>()

    CastClass,      //R castclass<T, R>(T obj)
    IsInstance,     //bool isinst<T>(T obj)

    LoadToken,

    Box,
    Unbox
}