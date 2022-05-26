namespace DistIL.IR;

using System.Text;

public class CallInst : Instruction
{
    public MethodDesc Method {
        get => (MethodDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public ReadOnlySpan<Value> Args => Operands.AsSpan(1);
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => Operands.Length - 1;

    public bool IsVirtual { get; set; }
    public bool IsStatic => Method.IsStatic;

    public override bool HasSideEffects => true;
    public override bool MayThrow => true;
    public override string InstName => "call" + (IsVirtual ? "virt" : "");

    public CallInst(MethodDesc method, Value[] args, bool isVirtual = false)
        : base(args.Prepend(method).ToArray())
    {
        ResultType = method.ReturnType;
        IsVirtual = isVirtual;
    }

    public Value GetArg(int index) => Operands[index + 1];
    public void SetArg(int index, Value newValue) => ReplaceOperand(index + 1, newValue);
    
    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(StringBuilder sb, SlotTracker slotTracker)
    {
        sb.Append($" {Method.DeclaringType}::{Method.Name}(");
        for (int i = 0; i < NumArgs; i++) {
            if (i != 0) sb.Append(", ");
            
            Method.Params[i].Type.Print(sb, slotTracker);
            sb.Append(": ");
            Args[i].PrintAsOperand(sb, slotTracker);
        }
        sb.Append(")");
    }
}
