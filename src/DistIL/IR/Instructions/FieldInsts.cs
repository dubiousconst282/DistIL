namespace DistIL.IR;

public abstract class FieldAccessInst : Instruction, AccessInst
{
    public FieldDesc Field {
        get => (FieldDesc)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value? Obj {
        get => IsStatic ? null : Operands[1];
        set {
            Ensure.That(!IsStatic && value != null);
            ReplaceOperand(1, value);
        }
    }
    [MemberNotNullWhen(false, nameof(Obj))]
    public bool IsStatic => Field.IsStatic;

    [MemberNotNullWhen(true, nameof(Obj))]
    public bool IsInstance => !IsStatic;

    public override bool MayThrow => IsInstance;

    Value AccessInst.Location => Field;

    protected FieldAccessInst(TypeDesc resultType, FieldDesc field, Value? obj)
        : this(resultType, obj == null ? new[] { field } : new[] { field, obj }) { }

    protected FieldAccessInst(TypeDesc resultType, params Value[] operands)
        : base(operands)
    {
        ResultType = resultType;
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

public class LoadFieldInst : FieldAccessInst, LoadInst
{
    public override string InstName => "ldfld";

    public LoadFieldInst(FieldDesc field, Value? obj = null)
        : base(field.Type, field, obj) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public class StoreFieldInst : FieldAccessInst, StoreInst
{
    public Value Value {
        get => Operands[IsStatic ? 1 : 2];
        set => ReplaceOperand(IsStatic ? 1 : 2, value);
    }

    public override bool SafeToRemove => false;
    public override bool MayWriteToMemory => true;
    public override string InstName => "stfld";

    public StoreFieldInst(FieldDesc field, Value? obj, Value value)
        : base(PrimType.Void, obj == null ? new[] { field, value } : new[] { field, obj, value })
    {
        Ensure.That(value.ResultType.IsStackAssignableTo(field.Type));
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public class FieldAddrInst : FieldAccessInst
{
    public override string InstName => "fldaddr";

    public FieldAddrInst(FieldDesc field, Value? obj)
        : base(field.Type.CreateByref(), field, obj) { }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}