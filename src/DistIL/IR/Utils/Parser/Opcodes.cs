namespace DistIL.IR.Utils.Parser;

#pragma warning disable format

internal enum Opcode
{
    Unknown,

    Goto, Ret, Phi,
    Call, CallVirt, NewObj,
    LdVar, StVar, VarAddr,
    Intrinsic,

    //Has modifiers
    ArrAddr, FldAddr,
    Load, Store,
    Conv,

    //Note: Entries must be keept in the same order as in BinaryOp
    _Bin_First,
    Bin_Add, Bin_Sub, Bin_Mul,
    Bin_SDiv, Bin_UDiv,
    Bin_SRem, Bin_URem,

    Bin_And, Bin_Or, Bin_Xor,
    Bin_Shl,    // <<   Shift left
    Bin_Shra,   // >>   Shift right (arithmetic)
    Bin_Shrl,   // >>>  Shift right (logical)

    Bin_FAdd, Bin_FSub, Bin_FMul, Bin_FDiv, Bin_FRem,

    Bin_AddOvf, Bin_SubOvf, Bin_MulOvf,
    Bin_UAddOvf, Bin_USubOvf, Bin_UMulOvf,
    _Bin_Last,

    //Note: Entries must be keept in the same order as in CompareOp
    _Cmp_First,
    Cmp_Eq, Cmp_Ne,
    Cmp_Slt, Cmp_Sgt, Cmp_Sle, Cmp_Sge,
    Cmp_Ult, Cmp_Ugt, Cmp_Ule, Cmp_Uge,

    Cmp_FOlt, Cmp_FOgt, Cmp_FOle, Cmp_FOge, Cmp_FOeq, Cmp_FOne,
    Cmp_FUlt, Cmp_FUgt, Cmp_FUle, Cmp_FUge, Cmp_FUeq, Cmp_FUne,
    _Cmp_Last,
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
            "ret"       => Opcode.Ret,
            "phi"       => Opcode.Phi,

            "call"      => Opcode.Call,
            "callvirt"  => Opcode.CallVirt,
            "newobj"    => Opcode.NewObj,
            
            "ldvar"     => Opcode.LdVar,
            "stvar"     => Opcode.StVar,
            "varaddr"   => Opcode.VarAddr,

            "intrinsic" => Opcode.Intrinsic,

            "add"       => Opcode.Bin_Add,
            "sub"       => Opcode.Bin_Sub,
            "mul"       => Opcode.Bin_Mul,
            "sdiv"      => Opcode.Bin_SDiv,
            "srem"      => Opcode.Bin_SRem,
            "udiv"      => Opcode.Bin_UDiv,
            "urem"      => Opcode.Bin_URem,
            "and"       => Opcode.Bin_And,
            "or"        => Opcode.Bin_Or,
            "xor"       => Opcode.Bin_Xor,
            "shl"       => Opcode.Bin_Shl,
            "shra"      => Opcode.Bin_Shra,
            "shrl"      => Opcode.Bin_Shrl,
            "fadd"      => Opcode.Bin_FAdd,
            "fsub"      => Opcode.Bin_FSub,
            "fmul"      => Opcode.Bin_FMul,
            "fdiv"      => Opcode.Bin_FDiv,
            "frem"      => Opcode.Bin_FRem,
            "add.ovf"   => Opcode.Bin_AddOvf,
            "sub.ovf"   => Opcode.Bin_SubOvf,
            "mul.ovf"   => Opcode.Bin_MulOvf,
            "uadd.ovf"  => Opcode.Bin_UAddOvf,
            "usub.ovf"  => Opcode.Bin_USubOvf,
            "umul.ovf"  => Opcode.Bin_UMulOvf,

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
}