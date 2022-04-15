namespace DistIL.IR;

public class BinaryInst : Instruction
{
    public BinaryOp Op { get; set; }

    public Value Left {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Right {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public override string InstName => Op.ToString().ToLower();

    public override bool MayThrow => Op is 
        (>= BinaryOp.SDiv and <= BinaryOp.URem) or
        (>= BinaryOp.FirstOvf_ and <= BinaryOp.LastOvf_);

    public BinaryInst(BinaryOp op, Value left, Value right)
        : base(left, right)
    {
        var lt = left.ResultType;
        var rt = right.ResultType;
        var res = lt;

        //Restrict operands to the same type, but special case pointers and byrefs mixed with integers
        if (lt != rt) {
            res = null;
            bool isRefL = lt is ByrefType;
            bool isRefR = rt is ByrefType;
            bool isOfsL = lt.StackType is StackType.Int or StackType.NInt;
            bool isOfsR = rt.StackType is StackType.Int or StackType.NInt;

            //ref + int | ref - int
            if (isRefL && isOfsR && op is BinaryOp.Add or BinaryOp.Sub) res = lt;
            else //int + ref
            if (isOfsL && isRefR && op is BinaryOp.Add) res = rt;
            else //no restriction for unmanaged pointers (they're nint on eval stack)
            if (isOfsL || isOfsR) res = rt.StackType > lt.StackType || rt is PointerType ? rt : lt; //select nint/ptr over int

            Ensure(res != null, "Invalid operand types for BinaryInst");
        }
        ResultType = res;
        Op = op;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public enum BinaryOp
{
    //Int
    Add, Sub, Mul,
    SDiv, UDiv,
    SRem, URem,

    And, Or, Xor,
    Shl,    // <<   Shift left
    Shra,   // >>   Shift right (arithmetic)
    Shrl,   // >>>  Shift right (logical)

    FAdd, FSub, FMul, FDiv, FRem,

    AddOvf, SubOvf, MulOvf,
    UAddOvf, USubOvf, UMulOvf,

    FirstOvf_ = AddOvf, LastOvf_ = UMulOvf
}