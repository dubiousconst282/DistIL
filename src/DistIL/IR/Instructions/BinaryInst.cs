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
        ResultType = GetResultType(op, left.ResultType, right.ResultType);
        Op = op;
    }

    private static RType GetResultType(BinaryOp op, RType a, RType b)
    {
        //ECMA335 III.1.5
        var sa = a.StackType;
        var sb = b.StackType;

        //Sort sa, sb to reduce number of cases,
        //such that sa <= sb with order [int long nint float nint byref]
        if (sa > sb) (sa, sb) = (sb, sa);

        #pragma warning disable format
        return (sa, sb, op) switch {
            (StackType.Int,     StackType.Int, _)   => PrimType.Int32,
            (StackType.Long,    StackType.Long, _)  => PrimType.Int64,
            (StackType.Float,   StackType.Float, _) => a == PrimType.Double ? a : b, //pick double over float
            (StackType.NInt,    StackType.NInt, _)  => a == b ? a : PrimType.IntPtr, //pick pointer over nint
            (StackType.ByRef,   StackType.ByRef, BinaryOp.Sub) => PrimType.IntPtr,   //& - & = nint
            //& + int/nint = &
            (StackType.ByRef,   StackType.Int or StackType.NInt, BinaryOp.Add or BinaryOp.AddOvf)
                    => a,
            _ => throw new InvalidOperationException("Invalid operand types in BinaryInst")
        };
        #pragma warning restore format
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