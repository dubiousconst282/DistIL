namespace DistIL.IR;

/// <summary> Extracts the value of a field in a struct. </summary>
/// <remarks> This instruction does not perform memory access, it just extracts the value of a struct field already in a SSA register. </remarks>
public class ExtractFieldInst : Instruction
{
    public FieldDesc Field { get; set; }

    public Value Obj {
        get => _operands[0];
        set => ReplaceOperand(0, value);
    }

    public override string InstName => "extractfield";

    public ExtractFieldInst(FieldDesc field, Value obj)
        : base(obj)
    {
        Ensure.That(obj.ResultType.IsValueType);

        ResultType = field.Type;
        Field = field;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print(" ");
        ctx.PrintAsOperand(Field);

        ctx.Print(", ");
        ctx.PrintAsOperand(Obj);
    }
}