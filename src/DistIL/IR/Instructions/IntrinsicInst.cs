namespace DistIL.IR;

using DistIL.IR.Intrinsics;

public class IntrinsicInst : Instruction
{
    public IntrinsicDesc Intrinsic { get; }
    public ReadOnlySpan<Value> Args => Operands;

    public override bool HasSideEffects => true;
    public override bool MayThrow => true;
    public override string InstName => "intrinsic";

    public IntrinsicInst(IntrinsicDesc intrinsic, params Value[] args)
        : base(args)
    {
        Intrinsic = intrinsic;
        ResultType = intrinsic.GetResultType(args);

        Ensure.That(intrinsic.ParamTypes.Length == args.Length);

        for (int i = 0; i < args.Length; i++) {
            Ensure.That(intrinsic.IsAcceptableArgument(args, i));
        }
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    public override void Print(PrintContext ctx)
    {
        if (this.Is(IRIntrinsicId.Marker) && Operands is [ConstString str]) {
            ctx.Print("//" + str.Value, PrintToner.Comment);
        } else {
            base.Print(ctx);
        }
    }

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print($" {PrintToner.MemberName}{Intrinsic.Namespace}::{PrintToner.MethodName}{Intrinsic.Name}(");
        for (int i = 0; i < _operands.Length; i++) {
            if (i > 0) ctx.Print(", ");
            ctx.PrintAsOperand(_operands[i]);
        }
        ctx.Print(")");
    }
}