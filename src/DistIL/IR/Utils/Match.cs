namespace DistIL.IR.Utils;

using DistIL.IR.Intrinsics;

public static class Match
{
    //public static bool FieldLoad(Value inst, out FieldDesc field, out Value? obj)
    //{
    //    
    //}

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