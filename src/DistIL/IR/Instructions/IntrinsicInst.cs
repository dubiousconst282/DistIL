namespace DistIL.IR;

/// <summary> Represents a </summary>
public abstract class IntrinsicInst : Instruction
{
    public ReadOnlySpan<Value> Args => _operands;

    public sealed override string InstName => "intrinsic";

    public abstract string Namespace { get; }
    public abstract string Name { get; }

    protected IntrinsicInst(TypeDesc resultType, Value[] args)
        : base(args)
    {
        ResultType = resultType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    /// <summary> Creates a new instance of this intrinsic with the given <see cref="Value.ResultType"/> and <see cref="Instruction.Operands"/> properties. </summary>
    /// <remarks> This method is meant to be used internally by <see cref="Utils.IRCloner"/>. The ownership of <paramref name="args"/> is given to the new instruction. </remarks> 
    internal IntrinsicInst CloneWith(TypeDesc resultType, Value[] args)
    {
        Debug.Assert(args.Length == _operands.Length);

        //MemberwiseClone() is only ~10-20% slower than actual ctors based on my tests (~30ms for 100k calls).
        //If it becomes a problem we could make this method abstract and use a T4 template/source generator to implement the boilerplate.
        var copy = (IntrinsicInst)MemberwiseDetachedClone();
        copy.Block = null!;
        copy.Prev = copy.Next = null;
        
        copy.ResultType = resultType;
        copy._operands = args;
        copy._useDefs = new UseDef[args.Length];

        for (int i = 0; i < args.Length; i++) {
            args[i].AddUse(copy, i);
        }
        return copy;
    }

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print($" {PrintToner.MemberName}{Namespace}::{PrintToner.MethodName}{Name}");
        ctx.PrintSequence("(", ")", _operands, ctx.PrintAsOperand);
    }
}