namespace DistIL.IR;

public class CompareInst : Instruction
{
    public CompareOp Op { get; set; }

    public Value Left {
        get => Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Right {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public override string InstName {
        get {
            var str = Op.ToString().ToLower();
            return Op switch {
                >= CompareOp.Slt and <= CompareOp.Uge => "icmp." + str,
                >= CompareOp.FOlt and <= CompareOp.FUne => "fcmp." + str[1..],
                _ => "cmp." + str
            };
        }
    }

    public CompareInst(CompareOp op, Value left, Value right)
        : base(left, right)
    {
        if (left.ResultType != right.ResultType) {
            CheckOperandTypes(op, left.ResultType.StackType, right.ResultType.StackType);
        }
        if (left.ResultType is VectorType vecType) {
            ResultType = VectorType.Create(PrimType.Bool, vecType.Width);
        } else {
            ResultType = PrimType.Bool;
        }
        Op = op;
    }

    private static void CheckOperandTypes(CompareOp op, StackType typeL, StackType typeR)
    {
        // Sort types to reduce number of permutations
        if (typeL > typeR) {
            (typeL, typeR) = (typeR, typeL);
        }
        // ECMA allows comparisons between Int and NInt
        Ensure.That(typeL == typeR || (typeL == StackType.Int && typeR == StackType.NInt));
        // Float comparison ops don't exist in CIL, but we want to enforce typing
        Ensure.That((typeL != StackType.Float && typeR != StackType.Float) || op.IsFloat());
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
/// <summary>
/// Specifies comparison operators for <see cref="CompareInst"/>.
/// <code>
/// Target types:
///   'Eq|Ne'    -> Integers, objects, pointers
///   'S(pred)'  -> Signed integer
///   'U(pred)'  -> Unsigned integer, pointers
///   'FO(pred)' -> Float ordered (result is true if neither operand is NaN and (pred) is true)
///   'FU(pred)' -> Float unordered (result is true if either operand is NaN or (pred) is true)
/// Predicates:
///   'Eq' -> Equal
///   'Ne' -> Not equal
///   'Lt' -> Less than
///   'Gt' -> Greater than
///   'Le' -> Less than or equal
///   'Ge' -> Greater than or equal
/// </code>
/// See also https://llvm.org/docs/LangRef.html#fcmp-instruction
/// </summary>
public enum CompareOp
{
    Eq, Ne,                 // int, ref, obj
    // <     >       <=      >=
    Slt,    Sgt,    Sle,    Sge,    // signed int
    Ult,    Ugt,    Ule,    Uge,    // unsigned int

    FOlt,   FOgt,   FOle,   FOge, FOeq,   FOne, // float ordered
    FUlt,   FUgt,   FUle,   FUge, FUeq,   FUne, // float unordered

    // TODO: Maybe remove FOne/FUeq since they don't exist in CIL and don't seem to be used very often.
}
public static class CompareOps
{
    /// <summary> Returns the negated operator: 
    /// Eq -> Ne, Slt -> Sge, Sgt -> Sle, 
    /// FOeq -> FUne, FOlt -> FUge, etc. 
    /// </summary>
    public static CompareOp GetNegated(this CompareOp op)
    {
        return op switch {
            CompareOp.Eq => CompareOp.Ne,
            CompareOp.Ne => CompareOp.Eq,
            CompareOp.Slt => CompareOp.Sge,
            CompareOp.Sgt => CompareOp.Sle,
            CompareOp.Sle => CompareOp.Sgt,
            CompareOp.Sge => CompareOp.Slt,
            CompareOp.Ult => CompareOp.Uge,
            CompareOp.Ugt => CompareOp.Ule,
            CompareOp.Ule => CompareOp.Ugt,
            CompareOp.Uge => CompareOp.Ult,

            CompareOp.FOeq => CompareOp.FUne,
            CompareOp.FOne => CompareOp.FUeq,
            CompareOp.FOlt => CompareOp.FUge,
            CompareOp.FOgt => CompareOp.FUle,
            CompareOp.FOle => CompareOp.FUgt,
            CompareOp.FOge => CompareOp.FUlt,
            CompareOp.FUeq => CompareOp.FOne,
            CompareOp.FUne => CompareOp.FOeq,
            CompareOp.FUlt => CompareOp.FOge,
            CompareOp.FUgt => CompareOp.FOle,
            CompareOp.FUle => CompareOp.FOgt,
            CompareOp.FUge => CompareOp.FOlt
        };
    }

    /// <summary> Returns the operator that gives the same result if the operands were swapped. </summary>
    public static CompareOp GetSwapped(this CompareOp op)
    {
        return op switch {
            CompareOp.Eq => CompareOp.Eq,
            CompareOp.Ne => CompareOp.Ne,
            CompareOp.Slt => CompareOp.Sgt,
            CompareOp.Sgt => CompareOp.Slt,
            CompareOp.Sle => CompareOp.Sge,
            CompareOp.Sge => CompareOp.Sle,
            CompareOp.Ult => CompareOp.Ugt,
            CompareOp.Ugt => CompareOp.Ult,
            CompareOp.Ule => CompareOp.Uge,
            CompareOp.Uge => CompareOp.Ule,

            CompareOp.FOeq => CompareOp.FOeq,
            CompareOp.FOne => CompareOp.FOne,
            CompareOp.FOlt => CompareOp.FOgt,
            CompareOp.FOgt => CompareOp.FOlt,
            CompareOp.FOle => CompareOp.FOge,
            CompareOp.FOge => CompareOp.FOle,
            CompareOp.FUeq => CompareOp.FUeq,
            CompareOp.FUne => CompareOp.FUne,
            CompareOp.FUlt => CompareOp.FUgt,
            CompareOp.FUgt => CompareOp.FUlt,
            CompareOp.FUle => CompareOp.FUge,
            CompareOp.FUge => CompareOp.FUle
        };
    }

    public static bool IsEquality(this CompareOp op)
        => op is CompareOp.Eq or CompareOp.Ne or
           CompareOp.FOeq or CompareOp.FOne or
           CompareOp.FUeq or CompareOp.FUne;

    /// <summary> Returns whether the operator expects operands to be float/double typed. </summary>
    public static bool IsFloat(this CompareOp op)
        => op is >= CompareOp.FOlt and <= CompareOp.FUne;

    /// <summary> Returns whether the operator interprets operands as signed integers. </summary>
    public static bool IsSigned(this CompareOp op)
        => op is >= CompareOp.Slt and <= CompareOp.Sge;

    /// <summary> Returns whether the operator interprets operands as unsigned integers. </summary>
    public static bool IsUnsigned(this CompareOp op)
        => op is >= CompareOp.Ult and <= CompareOp.Uge;

    /// <summary> Returns the unsigned version of the current operator, if it is signed; otherwise returns it unchanged. </summary>
    public static CompareOp GetUnsigned(this CompareOp op)
        => op.IsSigned() ? op + (CompareOp.Ult - CompareOp.Slt) : op;

    /// <summary> Returns the signed version of the current operator, if it is unsigned; otherwise returns it unchanged. </summary>
    public static CompareOp GetSigned(this CompareOp op)
        => op.IsUnsigned() ? op + (CompareOp.Slt - CompareOp.Ult) : op;

    /// <summary> Returns whether the operator is strict (non-inclusive, not "-or-equal"). </summary>
    public static bool IsStrict(this CompareOp op)
        => op is CompareOp.Slt or CompareOp.Sgt or CompareOp.Ult or CompareOp.Ugt or
                 CompareOp.FOlt or CompareOp.FOgt or CompareOp.FUlt or CompareOp.FUgt;

    /// <summary> Returns the strict version of this operator: Sle -> Slt, Sge -> Sgt, ... </summary>
    public static CompareOp GetStrict(this CompareOp op)
        => op switch {
            CompareOp.Sle => CompareOp.Slt,
            CompareOp.Sge => CompareOp.Sgt,
            CompareOp.Ule => CompareOp.Ult,
            CompareOp.Uge => CompareOp.Ugt,
            CompareOp.FOle => CompareOp.FOlt,
            CompareOp.FOge => CompareOp.FOgt,
            CompareOp.FUle => CompareOp.FUlt,
            CompareOp.FUge => CompareOp.FUgt,
            _ => op
        };

    /// <summary> Returns the non-strict version of this operator: Slt -> Sle, Sgt -> Sge, ... </summary>
    public static CompareOp GetNonStrict(this CompareOp op)
        => op switch {
            CompareOp.Slt => CompareOp.Sle,
            CompareOp.Sgt => CompareOp.Sge,
            CompareOp.Ult => CompareOp.Ule,
            CompareOp.Ugt => CompareOp.Uge,
            CompareOp.FOlt => CompareOp.FOle,
            CompareOp.FOgt => CompareOp.FOge,
            CompareOp.FUlt => CompareOp.FUle,
            CompareOp.FUgt => CompareOp.FUge,
            _ => op
        };
}