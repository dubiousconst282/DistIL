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

    public override void Print(PrintContext ctx)
    {
        if (Id == IntrinsicId.Marker && Operands is [ConstString str]) {
            ctx.Print("//" + str.Value, PrintToner.Comment);
        } else {
            base.Print(ctx);
        }
    }

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print(" ");
        ctx.Print(Id.ToString(), PrintToner.MethodName);
        ctx.Print("(");
        for (int i = 0; i < _operands.Length; i++) {
            if (i > 0) ctx.Print(", ");
            _operands[i].PrintAsOperand(ctx);
        }
        ctx.Print(")");
    }
}

public enum IntrinsicId
{
    Marker,         //nop, used for debugging
    CopyDef,        //T copy<T>(T value);  Copies an SSA value. Used to split live ranges during out of SSA translation.

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