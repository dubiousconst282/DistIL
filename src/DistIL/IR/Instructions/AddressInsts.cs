namespace DistIL.IR;

/// <summary> Base class for an instruction that computes the address of a field, array index, or pointer offset. </summary>
public abstract class AddressInst : Instruction
{
    public TypeDesc ElemType => ((PointerType)ResultType).ElemType;

    protected AddressInst(params Value[] opers)
        : base(opers)
    {
    }

    /// <summary> Returns the pointer offset factors or <see langword="default"/> if not known at compile time. </summary>
    /// <remarks> These can be used for the formula: <c>basePtr + BaseDisp + index * Stride</c> </remarks>
    public virtual (int Stride, int BaseDisp) GetKnownOffsetFactors() => default;
}
public class ArrayAddrInst : AddressInst
{
    public Value Array {
        get => _operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Index {
        get => _operands[1];
        set => ReplaceOperand(1, value);
    }

    /// <summary> Indicates whether <see cref="Array"/> is known to be non-null and have in-bounds index. </summary>
    public bool InBounds { get; set; }

    /// <summary> Hints that the result address will only be read from and no variance checks are necessary. </summary>
    public bool IsReadOnly { get; set; }

    /// <summary> Checks if <see cref="AddressInst.ElemType"/> differs from the array type. </summary>
    public bool IsCasting => ElemType != Array.ResultType.ElemType;

    public override bool MayThrow => !InBounds;
    public override string InstName => "arraddr" + (IsReadOnly ? ".readonly" : "") + (InBounds ? ".inbounds" : "");

    public ArrayAddrInst(Value array, Value index, TypeDesc? elemType = null, bool inBounds = false, bool readOnly = false)
        : base(array, index)
    {
        elemType ??= ((ArrayType)array.ResultType).ElemType;
        ResultType = elemType.CreateByref();
        InBounds = inBounds;
        IsReadOnly = readOnly;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}

public class FieldAddrInst : AddressInst
{
    public Value? Obj {
        get => IsStatic ? null : _operands[0];
        set {
            Ensure.That(!IsStatic && value != null);
            ReplaceOperand(0, value);
        }
    }
    public FieldDesc Field { get; set; }

    /// <summary> Indicates whether <see cref="Obj"/> is known to be a non-null object instance containing <see cref="Field"/>. </summary>
    public bool InBounds { get; set; }

    [MemberNotNullWhen(false, nameof(Obj))]
    public bool IsStatic => Field.IsStatic;

    [MemberNotNullWhen(true, nameof(Obj))]
    public bool IsInstance => !IsStatic;

    public override bool MayThrow => IsInstance && !InBounds;

    public override string InstName => "fldaddr" + (InBounds ? ".inbounds" : "");

    public FieldAddrInst(FieldDesc field, Value? obj = null, bool inBounds = false)
        : base(obj == null ? Array.Empty<Value>() : new[] { obj })
    {
        Ensure.That(field.IsInstance == (obj != null));
        Ensure.That(obj == null || obj.ResultType.IsPointerOrObject());

        ResultType = field.Type.CreateByref();
        Field = field;
        InBounds = inBounds || obj is LocalSlot;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print(" ");
        ctx.PrintAsOperand(Field);

        if (IsInstance) {
            ctx.Print(", ");
            ctx.PrintAsOperand(Obj);
        }
    }
}

/// <summary> Computes the offset address of a pointer. </summary>
public class PtrOffsetInst : AddressInst
{
    public Value BasePtr {
        get => _operands[0];
        set => ReplaceOperand(0, value);
    }
    public Value Index {
        get => _operands[1];
        set => ReplaceOperand(1, value);
    }

    /// <summary> Known index stride. If zero, this is computed at runtime to be the size of <see cref="AddressInst.ElemType"/>. </summary>
    public int Stride { get; set; } = 0;

    /// <summary> Whether the index stride is a constant known at compile time. </summary>
    public bool KnownStride => Stride > 0;

    public override string InstName => "lea";

    public PtrOffsetInst(Value basePtr, Value index, TypeDesc strideType)
        : base(basePtr, index)
    {
        ResultType = basePtr.ResultType is ByrefType 
            ? strideType.CreateByref()
            : strideType.CreatePointer();
        Stride = strideType.Kind.Size();
    }
    public PtrOffsetInst(Value basePtr, Value index, int stride)
        : base(basePtr, index)
    {
        Ensure.That(stride > 0);
        ResultType = basePtr.ResultType as PointerType ?? PrimType.Void.CreatePointer();
        Stride = stride;
    }
    /// <summary> Internal unchecked cloning constructor. </summary>
    internal PtrOffsetInst(Value basePtr, Value index, TypeDesc resultType, int stride, int _)
        : base(basePtr, index)
    {
        ResultType = resultType;
        Stride = stride;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        ctx.Print($" {BasePtr} + {Index}");

        if (Stride != 0) {
            ctx.Print($" * {PrintToner.Number}{Stride.ToString()}");
        }
    }
}