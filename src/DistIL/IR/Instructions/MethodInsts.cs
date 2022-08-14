namespace DistIL.IR;

public class CallInst : Instruction
{
    public MethodDesc Method {
        get => (MethodDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public ReadOnlySpan<Value> Args => Operands.Slice(1);
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => Operands.Length - 1;

    public bool IsVirtual { get; set; }
    public bool IsStatic => Method.IsStatic;
    public TypeDesc? Constraint { get; set; }

    public override bool HasSideEffects => true;
    public override bool MayThrow => true;
    public override bool MayWriteToMemory => true;
    public override string InstName => "call" + (IsVirtual ? "virt" : "");

    public CallInst(MethodDesc method, Value[] args, bool isVirtual = false, TypeDesc? constraint = null)
        : base(args.Prepend(method).ToArray())
    {
        Ensure(args.Length == method.Params.Length);
        ResultType = method.ReturnType;
        IsVirtual = isVirtual;
        Constraint = constraint;
    }

    public Value GetArg(int index) => Operands[index + 1];
    public void SetArg(int index, Value newValue) => ReplaceOperand(index + 1, newValue);
    
    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
        => PrintOperands(ctx, Method, Args, Constraint);

    internal static void PrintOperands(PrintContext ctx, MethodDesc method, ReadOnlySpan<Value> args, TypeDesc? constraint, bool isCtor = false)
    {
        ctx.Print(" ");
        method.DeclaringType.Print(ctx, includeNs: false);
        ctx.Print("::");
        ctx.Print(method.Name, PrintToner.MethodName);
        if (method is MethodSpec { GenericParams.Length: > 0 }) {
            ctx.PrintSequence("<", ">", method.GenericParams, p => p.Print(ctx, includeNs: false));
        }
        ctx.Print("(");
        for (int i = 0; i < args.Length; i++) {
            if (i != 0) ctx.Print(", ");

            if (i == 0 && method.IsInstance && !isCtor) {
                ctx.Print("this", PrintToner.Keyword);
            } else {
                var paramType = method.Params[i + (isCtor ? 1 : 0)].Type;
                paramType.Print(ctx, includeNs: false);
            }
            ctx.Print(": ");
            args[i].PrintAsOperand(ctx);
        }
        ctx.Print(")");
        if (constraint != null) {
            ctx.Print(" constrained ", PrintToner.Keyword);
            constraint.Print(ctx, includeNs: false);
        }
    }
}

public class NewObjInst : Instruction
{
    /// <summary> The `.ctor` method. Note that the first argument (`this`) is ignored. </summary>
    public MethodDesc Constructor {
        get => (MethodDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public ReadOnlySpan<Value> Args => Operands.Slice(1);

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int NumArgs => Operands.Length - 1;

    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;
    public override string InstName => "newobj";

    public NewObjInst(MethodDesc ctor, Value[] args)
        : base(args.Prepend(ctor).ToArray())
    {
        Ensure(args.Length == ctor.StaticParams.Length);
        ResultType = ctor.DeclaringType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
        => CallInst.PrintOperands(ctx, Constructor, Args, null, true);
}

public class FuncAddrInst : Instruction
{
    public MethodDesc Method {
        get => (MethodDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value? Object {
        get => IsVirtual ? Operands[1] : null;
        set {
            Ensure(IsVirtual && value != null);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(true, nameof(Object))]
    public bool IsVirtual => Operands.Length >= 2;

    public override string InstName => IsVirtual ? "virtfuncaddr" : "funcaddr";

    public FuncAddrInst(MethodDesc method, Value? obj = null)
        : base(obj == null ? new Value[] { method } : new Value[] { method, obj })
    {
        ResultType = new FuncPtrType(method);
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}