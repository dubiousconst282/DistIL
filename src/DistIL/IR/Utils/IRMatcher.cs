namespace DistIL.IR.Utils;

public static class IRMatcher
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
        if (inst is LoadInst { Address: FieldAddrInst { IsStatic: true } flda }) {
            field = flda.Field;
            return true;
        }
        field = null!;
        return false;
    }
    public static bool StaticFieldStore(Value? inst, out StoreInst store, out FieldDesc field)
    {
        if (inst is StoreInst { Address: FieldAddrInst { IsStatic: true } flda } st) {
            store = st;
            field = flda.Field;
            return true;
        }
        store = null!;
        field = null!;
        return false;
    }
}