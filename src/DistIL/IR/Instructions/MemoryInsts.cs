namespace DistIL.IR;

public abstract class MemoryInst : Instruction
{
    public Value Address {
        get => _operands[0];
        set => ReplaceOperand(0, value);
    }
    public override bool MayThrow =>
        Address switch {
            AddressInst addr and (FieldAddrInst or ArrayAddrInst)
                => addr.MayThrow || ElemType != addr.ElemType,

            LocalSlot slot
                => ElemType != slot.Type,

            _ => true
        };

    public abstract TypeDesc ElemType { get; }

    public PointerFlags Flags { get; set; }

    public bool IsUnaligned => (Flags & PointerFlags.Unaligned) != 0;
    public bool IsVolatile => (Flags & PointerFlags.Volatile) != 0;

    protected MemoryInst(PointerFlags flags, params Value[] operands)
        : base(operands)
    {
        Flags = flags;
    }

    protected string FormatName(string name)
    {
        if (IsUnaligned) name += ".un";
        if (IsVolatile) name += ".volatile";
        return name;
    }
}
public class LoadInst : MemoryInst
{
    public override TypeDesc ElemType => ResultType;

    public override string InstName => FormatName("load");
    public override bool MayReadFromMemory => true;

    public LoadInst(Value addr, TypeDesc? elemType = null, PointerFlags flags = 0)
        : base(flags, addr)
    {
        ResultType = elemType ?? ((PointerType)addr.ResultType).ElemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StoreInst : MemoryInst
{
    public Value Value {
        get => _operands[1];
        set => ReplaceOperand(1, value);
    }
    public override TypeDesc ElemType { get; }

    /// <summary> Checks if <see cref="AddressInst.ElemType"/> differs from the value type, ignoring sign/unsigned mismatches. </summary>
    public bool IsCasting {
        get {
            if (ElemType == Value.ResultType) {
                return false;
            }
            if (ElemType.IsInt() || ElemType.IsPointerLike()) {
                return ElemType.Kind.GetSigned() != Value.ResultType.Kind.GetSigned();
            }
            return true;
        }
    }

    public override string InstName => FormatName("store");
    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;

    public StoreInst(Value addr, Value value, TypeDesc? elemType = null, PointerFlags flags = 0)
        : base(flags, addr, value)
    {
        ElemType = elemType ?? ((PointerType)addr.ResultType).ElemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        base.PrintOperands(ctx);

        if (IsCasting) {
            ctx.Print(" as ", PrintToner.InstName);
            ElemType.Print(ctx);
        }
    }

    /// <summary>
    /// Creates a sequence of instructions that truncates or rounds the value to what it would have been after
    /// a store/load roundtrip to a location of type <paramref name="destType"/>, or returns <paramref name="val"/> itself if no truncation would occur.
    /// </summary>
    public static Value Coerce(TypeDesc destType, Value val, Instruction insertBefore)
    {
        if (!MustBeCoerced(destType, val)) {
            return val;
        }
        var conv = new ConvertInst(val, destType);
        conv.InsertBefore(insertBefore);
        return conv;
    }
    public static bool MustBeCoerced(TypeDesc destType, Value srcValue)
    {
        if (destType.Kind.IsSmallInt() && srcValue is ConstInt cons) {
            return !cons.FitsInType(destType);
        }
        return MustBeCoerced(destType, srcValue.ResultType);
    }
    public static bool MustBeCoerced(TypeDesc destType, TypeDesc srcType)
    {
        // III.1.6 Implicit argument coercion

        // `NInt -> &` as in "Start GC Tracking" sounds particularly brittle. Not even Roslyn makes guarantees about it:
        //  https://github.com/dotnet/runtime/issues/34501#issuecomment-608548207
        // It's probably for the best if we don't support it.
        return
            (destType.Kind.IsSmallInt() && !srcType.Kind.IsSmallInt()) ||
            (destType.StackType == StackType.Int && srcType.StackType == StackType.NInt) ||
            (destType.Kind == TypeKind.Single && srcType.Kind == TypeKind.Double);
    }
}

[Flags]
public enum PointerFlags : byte
{
    None = 0,
    Unaligned   = 1 << 0,
    Volatile    = 1 << 1
}