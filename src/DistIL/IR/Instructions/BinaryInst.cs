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
    public override string InstName => Op.ToString().ToLower().Replace("ovf", ".ovf");

    public override bool MayThrow =>
        // (x / 0) or (x / -1, when x == INT_MIN) may throw
        (Op is >= BinaryOp.SDiv and <= BinaryOp.URem && Right is not ConstInt { Value: not (0 or -1) }) ||
        ChecksOverflow;

    public bool IsCommutative => Op.IsCommutative();
    public bool IsAssociative => Op.IsAssociative();
    public bool ChecksOverflow => Op is >= BinaryOp.AddOvf and <= BinaryOp.UMulOvf;

    public BinaryInst(BinaryOp op, Value left, Value right)
        : base(left, right)
    {
        ResultType = GetResultType(op, left.ResultType, right.ResultType) 
            ?? throw new InvalidOperationException("Invalid operand types in BinaryInst");
        Op = op;
    }

    private static TypeDesc? GetResultType(BinaryOp op, TypeDesc a, TypeDesc b)
    {
        // Return original type for bit ops that never overflow (useful for bools)
        if (op is BinaryOp.And or BinaryOp.Or or BinaryOp.Xor && a == b) {
            return a;
        }

        // ECMA335 III.1.5
        var sa = a.StackType;
        var sb = b.StackType;

        if (sa == sb) {
            return sa switch {
                StackType.Int or StackType.Long
                    => sa.GetPrimType(a.Kind.IsSigned() || b.Kind.IsSigned()),
                StackType.Float
                    => a == PrimType.Double ? a : b, // pick double over float
                StackType.NInt
                    => a is PointerType ? a : PrimType.IntPtr, // pick pointer over nint
                StackType.ByRef when op == BinaryOp.Sub
                    => PrimType.IntPtr,
                StackType.Struct when a is VectorType && a == b
                    => a,
                _ => null
            };
        }

        // Bit shift ops allows any combination of (i4/i8/nint op i4/nint)
        if (op is BinaryOp.Shl or BinaryOp.Shra or BinaryOp.Shrl &&
            sa is StackType.Int or StackType.Long or StackType.NInt &&
            sb is StackType.Int or StackType.NInt
        ) {
            // Type must be normalized to stack type, otherwise we'd endup with non-sense:
            //  byte r1 = shl #byte_x, 8
            return sa.GetPrimType(a.Kind.IsSigned());
        }

        // Sort (a, b) to reduce number of cases, such that sa <= sb
        // in respect to declaration order: [int long nint float nint byref]
        if (sa > sb) { (sa, sb, a, b) = (sb, sa, b, a); }

        return (sa, sb, op) switch {
            (StackType.Int, StackType.NInt, _)
                => b is PointerType ? b : sb.GetPrimType(b.Kind.IsSigned()),
            // int/nint + & = &
            (StackType.Int or StackType.NInt, StackType.ByRef, BinaryOp.Add or BinaryOp.AddOvf)
                => b,
            _ => null
        };
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public enum BinaryOp
{
    // Int
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
}
public static class BinaryOps
{
    public static bool IsCommutative(this BinaryOp op)
    {
        return op is
            BinaryOp.Add or BinaryOp.Mul or
            BinaryOp.FAdd or BinaryOp.FMul or
            BinaryOp.And or BinaryOp.Or or BinaryOp.Xor or
            BinaryOp.AddOvf or BinaryOp.MulOvf;
    }
    public static bool IsAssociative(this BinaryOp op)
    {
        return op is
            BinaryOp.Add or BinaryOp.Mul or
            BinaryOp.And or BinaryOp.Or or BinaryOp.Xor or
            BinaryOp.AddOvf or BinaryOp.MulOvf;
    }
}