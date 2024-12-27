namespace DistIL.IR.Utils.Parser;

#pragma warning disable format

internal enum Opcode
{
    Unknown,

    Goto, Switch, Ret, Phi,
    Call, CallVirt, NewObj,
    Intrinsic,
    Select,
    Lea,
    Getfld, Setfld,

    // Has modifiers
    ArrAddr, FldAddr,
    Load, Store,
    Conv,

    // BinaryOp
    // NOTE: order must match respective enums
    _FirstBinaryOp,
    Add, Sub, Mul,
    SDiv, UDiv,
    SRem, URem,

    And, Or, Xor,
    Shl,    // <<   Shift left
    Shra,   // >>   Shift right (arithmetic)
    Shrl,   // >>>  Shift right (logical)

    FAdd, FSub, FMul, FDiv, FRem,

    AddOvf, SubOvf, MulOvf,
    UAddOvf, USubOvf, UMulOvf,
    _LastBinaryOp,

    // UnaryOp
    _FirstUnaryOp,
    Neg, Not, FNeg,
    _LastUnaryOp,

    // CompareOp
    _FirstCompareOp,
    Cmp_Eq, Cmp_Ne,
    Cmp_Slt, Cmp_Sgt, Cmp_Sle, Cmp_Sge,
    Cmp_Ult, Cmp_Ugt, Cmp_Ule, Cmp_Uge,

    Cmp_FOlt, Cmp_FOgt, Cmp_FOle, Cmp_FOge, Cmp_FOeq, Cmp_FOne,
    Cmp_FUlt, Cmp_FUgt, Cmp_FUle, Cmp_FUge, Cmp_FUeq, Cmp_FUne,
    _LastCompareOp,
}

[Flags]
internal enum OpcodeModifiers
{
    None        = 0,
    Ovf         = 1 << 0,
    Un          = 1 << 1,
    Volatile    = 1 << 2,
    InBounds    = 1 << 3,
    ReadOnly    = 1 << 4,
}
internal static class Opcodes
{
    public static (Opcode Op, OpcodeModifiers Mods) TryParse(string str)
    {
        var op = str switch {
            "goto"      => Opcode.Goto,
            "switch"    => Opcode.Switch,
            "ret"       => Opcode.Ret,
            "phi"       => Opcode.Phi,

            "call"      => Opcode.Call,
            "callvirt"  => Opcode.CallVirt,
            "newobj"    => Opcode.NewObj,
            
            "intrinsic" => Opcode.Intrinsic,
            "select"    => Opcode.Select,
            "lea"       => Opcode.Lea,

            "getfld"    => Opcode.Getfld,
            "setfld"    => Opcode.Setfld,

            "add"       => Opcode.Add,
            "sub"       => Opcode.Sub,
            "mul"       => Opcode.Mul,
            "sdiv"      => Opcode.SDiv,
            "srem"      => Opcode.SRem,
            "udiv"      => Opcode.UDiv,
            "urem"      => Opcode.URem,
            "and"       => Opcode.And,
            "or"        => Opcode.Or,
            "xor"       => Opcode.Xor,
            "shl"       => Opcode.Shl,
            "shra"      => Opcode.Shra,
            "shrl"      => Opcode.Shrl,
            "fadd"      => Opcode.FAdd,
            "fsub"      => Opcode.FSub,
            "fmul"      => Opcode.FMul,
            "fdiv"      => Opcode.FDiv,
            "frem"      => Opcode.FRem,
            "add.ovf"   => Opcode.AddOvf,
            "sub.ovf"   => Opcode.SubOvf,
            "mul.ovf"   => Opcode.MulOvf,
            "uadd.ovf"  => Opcode.UAddOvf,
            "usub.ovf"  => Opcode.USubOvf,
            "umul.ovf"  => Opcode.UMulOvf,

            "not"       => Opcode.Not,
            "neg"       => Opcode.Neg,
            "fneg"       => Opcode.FNeg,

            "cmp.eq"    => Opcode.Cmp_Eq,
            "cmp.ne"    => Opcode.Cmp_Ne,
            "icmp.slt"  => Opcode.Cmp_Slt,
            "icmp.sgt"  => Opcode.Cmp_Sgt,
            "icmp.sle"  => Opcode.Cmp_Sle,
            "icmp.sge"  => Opcode.Cmp_Sge,
            "icmp.ult"  => Opcode.Cmp_Ult,
            "icmp.ugt"  => Opcode.Cmp_Ugt,
            "icmp.ule"  => Opcode.Cmp_Ule,
            "icmp.uge"  => Opcode.Cmp_Uge,

            "fcmp.olt"  => Opcode.Cmp_FOlt,
            "fcmp.ogt"  => Opcode.Cmp_FOgt,
            "fcmp.ole"  => Opcode.Cmp_FOle,
            "fcmp.oge"  => Opcode.Cmp_FOge,
            "fcmp.oeq"  => Opcode.Cmp_FOeq,
            "fcmp.one"  => Opcode.Cmp_FOne,

            "fcmp.ult"  => Opcode.Cmp_FUlt,
            "fcmp.ugt"  => Opcode.Cmp_FUgt,
            "fcmp.ule"  => Opcode.Cmp_FUle,
            "fcmp.uge"  => Opcode.Cmp_FUge,
            "fcmp.ueq"  => Opcode.Cmp_FUeq,
            "fcmp.une"  => Opcode.Cmp_FUne,

            _ => Opcode.Unknown
        };
        var mods = OpcodeModifiers.None;

        if (op == Opcode.Unknown) {
            int offset = str.IndexOf('.');
            if (offset < 0) offset = str.Length;

            op = str.AsSpan(0, offset) switch {
                "load" => Opcode.Load,
                "store" => Opcode.Store,
                "arraddr" => Opcode.ArrAddr,
                "fldaddr" => Opcode.FldAddr,
                "conv"  => Opcode.Conv,
                _ => Opcode.Unknown
            };
            if (op != Opcode.Unknown && str.Length > offset) {
                mods = ParseModifiers(str);
            }
        }
        return (op, mods);
    }

    private static OpcodeModifiers ParseModifiers(string str)
    {
        return (str.Contains(".ovf")        ? OpcodeModifiers.Ovf : 0) |
               (str.Contains(".un")         ? OpcodeModifiers.Un : 0) |
               (str.Contains(".volatile")   ? OpcodeModifiers.Volatile : 0) |
               (str.Contains(".inbounds")   ? OpcodeModifiers.InBounds : 0) |
               (str.Contains(".readonly")   ? OpcodeModifiers.ReadOnly : 0);
    }

    public static bool IsBinaryOp(this Opcode op) => op is > Opcode._FirstBinaryOp and < Opcode._LastBinaryOp;
    public static bool IsUnaryOp(this Opcode op) => op is > Opcode._FirstUnaryOp and < Opcode._LastUnaryOp;
    public static bool IsCompareOp(this Opcode op) => op is > Opcode._FirstCompareOp and < Opcode._LastCompareOp;

    public static BinaryOp GetBinaryOp(this Opcode op)
    {
        Ensure.That(op.IsBinaryOp());
        return (BinaryOp)(op - (Opcode._FirstBinaryOp + 1));
    }
    public static UnaryOp GetUnaryOp(this Opcode op)
    {
        Ensure.That(op.IsUnaryOp());
        return (UnaryOp)(op - (Opcode._FirstUnaryOp + 1));
    }
    public static CompareOp GetCompareOp(this Opcode op)
    {
        Ensure.That(op.IsCompareOp());
        return (CompareOp)(op - (Opcode._FirstCompareOp + 1));
    }
}