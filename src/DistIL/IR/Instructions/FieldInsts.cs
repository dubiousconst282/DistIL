namespace DistIL.IR;

public class LoadFieldInst : Instruction
{
    public Field Field {
        get => (Field)Operands[0];
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

    public override string InstName => "ldfld";
    public override bool MayThrow => true;

    public LoadFieldInst(Field field, Value? obj = null)
        : base(obj == null ? new[] { field } : new[] { field, obj })
    {
        Ensure(field.IsStatic == (obj == null));
        ResultType = field.Type;
    }
    private LoadFieldInst(LoadFieldInst inst)
        : base(inst.Operands)
    {
        ResultType = inst.ResultType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public class StoreFieldInst : Instruction
{
    public Field Field {
        get => (Field)Operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Value {
        get => Operands[1];
        set => ReplaceOperand(1, value);
    }
    public Value? Obj {
        get => IsStatic ? null : Operands[2];
        set {
            Ensure(!IsStatic && value != null);
            ReplaceOperand(2, value);
        }
    }
    [MemberNotNullWhen(false, nameof(Obj))]
    public bool IsStatic => Field.IsStatic;

    public override string InstName => "stfld";
    public override bool MayThrow => true;

    public StoreFieldInst(Field field, Value value, Value? obj = null)
        : base(obj == null ? new[] { field, value } : new[] { field, value, obj })
    {
        Ensure((field.IsStatic == (obj == null)) && value.ResultType.IsStackAssignableTo(field.Type));
    }
    private StoreFieldInst(StoreFieldInst inst)
        : base(inst.Operands)
    {
    }
    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}