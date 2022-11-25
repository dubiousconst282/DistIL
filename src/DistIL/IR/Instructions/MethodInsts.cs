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
        Ensure.That(args.Length == method.ParamSig.Count);
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
                var paramType = method.ParamSig[i + (isCtor ? 1 : 0)].Type;
                paramType.Print(ctx);
            }
            ctx.Print(": ");
            args[i].PrintAsOperand(ctx);
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
        Ensure.That(!ctor.IsStatic && args.Length == (ctor.ParamSig.Count - 1));
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
            Ensure.That(IsVirtual && value != null);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(true, nameof(Object))]
    public bool IsVirtual => Operands.Length >= 2;

    public override string InstName => IsVirtual ? "virtfuncaddr" : "funcaddr";

    public FuncAddrInst(MethodDesc method, Value? obj = null)
        : base(obj == null ? new Value[] { method } : new Value[] { method, obj })
    {
        ResultType = PrimType.Void.CreatePointer();
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}