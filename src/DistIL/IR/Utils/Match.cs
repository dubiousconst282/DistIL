namespace DistIL.IR.Utils;

using DistIL.IR.Intrinsics;

public static class Match
{
    public static bool Add(Value? value, out Value left, out Value right)
        => Binary(value, BinaryOp.Add, out left, out right);
    public static bool Mul(Value? value, out Value left, out Value right)
        => Binary(value, BinaryOp.Mul, out left, out right);

    public static bool Binary(Value? value, BinaryOp op, out Value left, out Value right)
    {
        if (value is BinaryInst bin && bin.Op == op) {
            (left, right) = (bin.Left, bin.Right);
            return true;
        }
        left = right = null!;
        return false;
    }

    public static bool StaticFieldLoad(Value? inst, out FieldDesc field)
    {
        if (inst is LoadPtrInst { Address: FieldAddrInst { IsStatic: true } flda }) {
            field = flda.Field;
            return true;
        }
        field = null!;
        return false;
    }
    public static bool StaticFieldStore(Value? inst, out StorePtrInst store, out FieldDesc field)
    {
        if (inst is StorePtrInst { Address: FieldAddrInst { IsStatic: true } flda } st) {
            store = st;
            field = flda.Field;
            return true;
        }
        store = null!;
        field = null!;
        return false;
    }

    public static bool Is(this Value inst, CilIntrinsicId id)
        => inst is IntrinsicInst { Intrinsic: CilIntrinsic c } && c.Id == id;

    public static bool Is(this Value inst, CilIntrinsicId id1, CilIntrinsicId id2)
        => inst is IntrinsicInst { Intrinsic: CilIntrinsic c } && (c.Id == id1 || c.Id == id2);

    public static bool Is(this Value inst, CilIntrinsicId id, [NotNullWhen(true)] out IntrinsicInst? res)
    {
        res = inst as IntrinsicInst;
        return res is { Intrinsic: CilIntrinsic c } intrin && c.Id == id;
    }

    public static bool Is(this Value inst, IRIntrinsicId id)
        => inst is IntrinsicInst { Intrinsic: IRIntrinsic c } && c.Id == id;

    /// <summary> If <paramref name="value"/> is a <see cref="CilIntrinsic.Box"/> instruction, returns the boxed value type; otherwise, returns the value result type. </summary>
    public static TypeDesc GetUnboxedType(this Value value)
        => value is IntrinsicInst {
            Intrinsic: CilIntrinsic { Id: CilIntrinsicId.Box },
            Args: [TypeDesc type, _]
        }
        ? type : value.ResultType;
}