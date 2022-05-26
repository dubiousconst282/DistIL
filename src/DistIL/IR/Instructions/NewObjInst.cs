namespace DistIL.IR;

using System.Text;

public class NewObjInst : Instruction
{
    /// <summary> The `.ctor` method. Note that the first argument (`this`) is ignored. </summary>
    public MethodDesc Constructor {
        get => (MethodDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public ReadOnlySpan<Value> Args => Operands.AsSpan(1);
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => Operands.Length - 1;

    public override bool HasSideEffects => true;
    public override string InstName => "newobj";

    public NewObjInst(MethodDesc ctor, Value[] args)
        : base(args.Prepend(ctor).ToArray())
    {
        ResultType = ctor.DeclaringType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append(" ");
        ResultType.Print(sb, slotTracker);
        sb.Append("(");

        for (int i = 0; i < NumArgs; i++) {
            sb.Append(i == 0 ? " " : ", ");

            Constructor.Params[i + 1].Type.Print(sb, slotTracker);
            sb.Append(": ");
            Args[i].PrintAsOperand(sb, slotTracker);
        }
        sb.Append(")");
    }
}
