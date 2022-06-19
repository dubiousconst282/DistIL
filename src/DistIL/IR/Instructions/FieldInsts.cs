namespace DistIL.IR;

public abstract class FieldAccessInst : Instruction
{
    public FieldDesc Field {
        get => (FieldDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value? Obj {
        get => IsStatic ? null : Operands[1];
        set {
            Ensure(!IsStatic && value != null);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(false, nameof(Obj))]
    public bool IsStatic => Field.IsStatic;

    [MemberNotNullWhen(true, nameof(Obj))]
    public bool IsInstance => !IsStatic;

    public override bool MayThrow => IsInstance;

    protected FieldAccessInst(params Value[] args)
        : base(args)
    {
    }

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print(" ");
        Field.PrintAsOperand(ctx);

        foreach (var oper in Operands[1..]) {
            ctx.Print(", ");
            oper.PrintAsOperand(ctx);
        }
    }
}

public class LoadFieldInst : FieldAccessInst
{
    public override string InstName => "ldfld";

    public LoadFieldInst(FieldDesc field, Value? obj)
        : base(obj == null ? new[] { field } : new[] { field, obj })
    {
        Ensure(field.IsStatic == (obj == null));
        ResultType = field.Type;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public class StoreFieldInst : FieldAccessInst
{
    public Value Value {
        get => Operands[IsStatic ? 1 : 2];
        set => ReplaceOperand(IsStatic ? 1 : 2, value);
    }

    public override bool SafeToRemove => false;
    public override bool MayWriteToMemory => true;
    public override string InstName => "stfld";

    public StoreFieldInst(FieldDesc field, Value? obj, Value value)
        : base(obj == null ? new[] { field, value } : new[] { field, obj, value })
    {
        Ensure((field.IsStatic == (obj == null)) && value.ResultType.IsStackAssignableTo(field.Type));
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public class FieldAddrInst : FieldAccessInst
{
    public override string InstName => "fldaddr";

    public FieldAddrInst(FieldDesc field, Value? obj)
        : base(obj == null ? new[] { field } : new[] { field, obj })
    {
        Ensure(field.IsStatic == (obj == null));
        ResultType = new ByrefType(field.Type);
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}