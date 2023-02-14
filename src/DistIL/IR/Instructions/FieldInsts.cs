namespace DistIL.IR;

public abstract class FieldAccessInst : Instruction, AccessInst
{
    public FieldDesc Field { get; set; }
    public Value? Obj {
        get => IsStatic ? null : _operands[0];
        set {
            Ensure.That(!IsStatic && value != null);
            ReplaceOperand(0, value);
        }
    }
    [MemberNotNullWhen(false, nameof(Obj))]
    public bool IsStatic => Field.IsStatic;

    [MemberNotNullWhen(true, nameof(Obj))]
    public bool IsInstance => !IsStatic;

    public override bool MayThrow => IsInstance;

    Value AccessInst.Location => Field;
    TypeDesc AccessInst.LocationType => Field.Type;

    protected FieldAccessInst(TypeDesc resultType, FieldDesc field, Value? obj)
        : this(resultType, field, obj == null ? Array.Empty<Value>() : new[] { obj }) { }

    protected FieldAccessInst(TypeDesc resultType, FieldDesc field, params Value[] operands)
        : base(operands)
    {
        ResultType = resultType;
        Field = field;
    }

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print(" ");
        ctx.PrintAsOperand(Field);

        foreach (var oper in _operands) {
            ctx.Print(", ");
            ctx.PrintAsOperand(oper);
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
        get => _operands[IsStatic ? 0 : 1];
        set => ReplaceOperand(IsStatic ? 0 : 1, value);
    }
    
    public override bool MayWriteToMemory => true;
    public override string InstName => "stfld";

    public StoreFieldInst(FieldDesc field, Value? obj, Value value)
        : base(PrimType.Void, field, obj == null ? new[] { value } : new[] { obj, value })
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