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
        Ensure.That(left.ResultType.StackType == right.ResultType.StackType);
        ResultType = PrimType.Int32;
        Op = op;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
/// <summary>
/// Specifies comparison operators for CompareInst.
/// 
/// Prefixes:
///   Eq|Ne    -> integers, objects, refs/pointers
///   S[pred]  -> Signed integer
///   U[pred]  -> Unsigned integer
///   FO[pred] -> Float ordered (result is `true` if neither operand is NaN and [pred] is `true`)
///   FU[pred] -> Float unordered (result is `true` if either operand is NaN or [pred] is `true`)
/// Predicates:
///   Eq -> Equal
///   Ne -> Not equal
///   Lt / Gt -> Less than / Greater than
///   Le / Ge -> Less than / Greater than; or equal
///   
/// See also https://llvm.org/docs/LangRef.html#fcmp-instruction
/// </summary>
public enum CompareOp
{
    Eq, Ne,                 //int, ref, obj
    //<     >       <=      >=
    Slt,    Sgt,    Sle,    Sge,    //signed int
    Ult,    Ugt,    Ule,    Uge,    //unsigned int

    FOlt,   FOgt,   FOle,   FOge, FOeq,   FOne, //float ordered
    FUlt,   FUgt,   FUle,   FUge, FUeq,   FUne, //float unordered
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
            CompareOp.Ugt => CompareOp.Ule,
            CompareOp.Ult => CompareOp.Uge,
            CompareOp.Uge => CompareOp.Ult,
            CompareOp.Ule => CompareOp.Ugt,
            CompareOp.Sgt => CompareOp.Sle,
            CompareOp.Slt => CompareOp.Sge,
            CompareOp.Sge => CompareOp.Slt,
            CompareOp.Sle => CompareOp.Sgt,

            CompareOp.FOeq => CompareOp.FUne,
            CompareOp.FOne => CompareOp.FUeq,
            CompareOp.FOgt => CompareOp.FUle,
            CompareOp.FOlt => CompareOp.FUge,
            CompareOp.FOge => CompareOp.FUlt,
            CompareOp.FOle => CompareOp.FUgt,
            CompareOp.FUeq => CompareOp.FOne,
            CompareOp.FUne => CompareOp.FOeq,
            CompareOp.FUgt => CompareOp.FOle,
            CompareOp.FUlt => CompareOp.FOge,
            CompareOp.FUge => CompareOp.FOlt,
            CompareOp.FUle => CompareOp.FOgt,

            _ => throw new InvalidOperationException()
        };
    }
}