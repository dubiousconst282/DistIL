namespace DistIL.IR;

using System.Text;

public class IntrinsicInst : Instruction
{
    public IntrinsicId Id { get; set; }
    public ReadOnlySpan<Value> Args => Operands;

    public override bool HasSideEffects => true;
    public override bool MayThrow => true;
    public override string InstName => "intrinsic";

    public IntrinsicInst(IntrinsicId intrinsic, TypeDesc resultType, params Value[] args)
        : base(args)
    {
        Id = intrinsic;
        ResultType = resultType;
    }

    public Value GetArg(int index) => Operands[index];
    public void SetArg(int index, Value newValue) => ReplaceOperand(index, newValue);

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(StringBuilder sb, SlotTracker slotTracker)
    {
        if (Id == IntrinsicId.Marker && Operands is [ConstString str]) {
            sb.Append("//" + str.Value);
        } else {
            base.Print(sb, slotTracker);
        }
    }

    protected override void PrintOperands(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append($" {Id}");
        int pos = sb.Length;
        base.PrintOperands(sb, slotTracker);
        sb[pos] = '('; //PrintOperands will prepend a space
        sb.Append(")");
    }
}

public enum IntrinsicId
{
    Marker,         //nop, used for debugging

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