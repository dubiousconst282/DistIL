namespace DistIL.IR;

using System.Text;

public class CallInst : Instruction
{
    public Callsite Method {
        get => (Callsite)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public ReadOnlySpan<Value> Args => Operands.AsSpan(1);
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => Operands.Length - 1;

    public bool IsVirtual { get; set; }
    public bool IsStatic => Method.IsStatic;

    public override bool HasSideEffects => !Method.IsPure;
    public override bool MayThrow => !Method.IsPure;
    public override string InstName => "call" + (IsVirtual ? "virt" : "");

    public CallInst(Callsite method, Value[] args, bool isVirtual = false)
        : base(args.Prepend(method).ToArray())
    {
        ResultType = method.RetType;
        IsVirtual = isVirtual;
    }

    public Value GetArg(int index) => Operands[index + 1];
    public void SetArg(int index, Value newValue) => ReplaceOperand(index + 1, newValue);
    
    public bool GetIntrinsic([NotNullWhen(true)] out Intrinsic? intrinsic)
    {
        intrinsic = Method as Intrinsic;
        return intrinsic != null;
    }
    public bool GetIntrinsic([NotNullWhen(true)] out Intrinsic? intrinsic, IntrinsicId id1, IntrinsicId id2 = 0, IntrinsicId id3 = 0)
    {
        intrinsic = Method as Intrinsic;
        return intrinsic != null && (intrinsic.Id == id1 || intrinsic.Id == id2 || intrinsic.Id == id3);
    }
    
    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(StringBuilder sb, SlotTracker slotTracker)
    {
        var decl = (Method as AsmIO.MethodDef)?.DeclaringType.ToString();
        sb.Append($" {(decl == null ? "" : decl + "::")}{Method.Name}(");
        for (int i = 0; i < NumArgs; i++) {
            if (i != 0) sb.Append(", ");
            
            Method.ArgTypes[i].Print(sb);
            sb.Append(": ");
            Args[i].PrintAsOperand(sb, slotTracker);
        }
        sb.Append(")");
    }
}
