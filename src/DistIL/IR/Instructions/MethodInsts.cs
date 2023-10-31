namespace DistIL.IR;

public class CallInst : Instruction
{
    public MethodDesc Method { get; set; }
    public ReadOnlySpan<Value> Args => _operands; //TODO: get rid of this, use Operands directly.

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => _operands.Length;

    public bool IsVirtual { get; set; }
    public bool IsStatic => Method.IsStatic;
    public TypeDesc? Constraint { get; set; }

    public override bool HasSideEffects => true;
    public override bool MayThrow => true;
    public override bool MayWriteToMemory => true;
    public override bool MayReadFromMemory => true;
    public override string InstName => "call" + (IsVirtual ? "virt" : "");

    public CallInst(MethodDesc method, Value[] args, bool isVirtual = false, TypeDesc? constraint = null)
        : base(args)
    {
        Ensure.That(args.Length == method.ParamSig.Count);
        ResultType = method.ReturnType;
        Method = method;
        IsVirtual = isVirtual;
        Constraint = constraint;
    }

    public void SetArg(int index, Value newValue) => ReplaceOperand(index, newValue);
    
    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
        => PrintOperands(ctx, Method, Args, Constraint);

    internal static void PrintOperands(PrintContext ctx, MethodDesc method, ReadOnlySpan<Value> args, TypeDesc? constraint, bool isCtor = false)
    {
        ctx.Print($" {method.DeclaringType}::{PrintToner.MethodName}{method.Name}");
        if (method is MethodSpec { IsGeneric: true }) {
            ctx.PrintSequence("<", ">", method.GenericParams, p => p.Print(ctx));
        }
        ctx.Print("(");
        for (int i = 0; i < args.Length; i++) {
            if (i != 0) ctx.Print(", ");

            if (i == 0 && method.IsInstance && !isCtor) {
                ctx.Print("this", PrintToner.Keyword);
            } else {
                var paramType = method.ParamSig[i + (isCtor ? 1 : 0)];
                paramType.Print(ctx);
            }
            ctx.Print(": ");
            ctx.PrintAsOperand(args[i]);
        }
        ctx.Print(")");
        if (constraint != null) {
            ctx.Print(" constrained ", PrintToner.Keyword);
            constraint.Print(ctx);
        }
    }
}

public class NewObjInst : Instruction
{
    /// <summary> The <c>.ctor</c> method used to initialize the object. </summary>
    public MethodDesc Constructor { get; set; }
    public ReadOnlySpan<Value> Args => _operands;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => _operands.Length;

    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;
    public override bool MayReadFromMemory => true;
    public override string InstName => "newobj";

    public NewObjInst(MethodDesc ctor, Value[] args)
        : base(args)
    {
        Ensure.That(!ctor.IsStatic && args.Length == (ctor.ParamSig.Count - 1));
        ResultType = ctor.DeclaringType;
        Constructor = ctor;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
        => CallInst.PrintOperands(ctx, Constructor, Args, null, true);
}

public class FuncAddrInst : Instruction
{
    public MethodDesc Method { get; set; }
    public Value? Object {
        get => IsVirtual ? Operands[0] : null;
        set {
            Ensure.That(IsVirtual && value != null);
            ReplaceOperand(0, value);
        }
    }
    [MemberNotNullWhen(true, nameof(Object))]
    public bool IsVirtual => Operands.Length > 0;

    public override string InstName => IsVirtual ? "virtfuncaddr" : "funcaddr";

    public FuncAddrInst(MethodDesc method, Value? obj = null)
        : base(obj == null ? Array.Empty<Value>() : new Value[] { obj })
    {
        ResultType = PrimType.Void.CreatePointer();
        Method = method;
    }

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print(" ");
        ctx.Print(Method);
        
        if (Object != null) {
            ctx.Print(", ");
            ctx.Print(Object);
        }
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}