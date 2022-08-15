namespace DistIL.IR.Utils.Parser;

partial class Materializer
{
    private static Instruction? CreateInstUnchecked(string opcode, Value[] opers, TypeDesc resultType)
    {
        return (opcode, opers.Length) switch {
            _ when GetBinaryOp(opcode) is BinaryOp binOp 
                => new BinaryInst(binOp, opers[0], opers[1]),

            _ when GetCompareOp(opcode) is CompareOp cmpOp 
                => new CompareInst(cmpOp, opers[0], opers[1]),

            ("phi", _) => new PhiInst(resultType, opers),
            ("call" or "callvirt", >= 1) => new CallInst((MethodDesc)opers[0], opers.AsSpan(1).ToArray(), opcode == "callvirt"),

            ("ret", 0 or 1) => new ReturnInst(opers.FirstOrDefault()),
            ("goto", 1) => new BranchInst((BasicBlock)opers[0]),
            ("goto", 3) => new BranchInst(opers[0], (BasicBlock)opers[1], (BasicBlock)opers[2]),
            
            ("ldvar", 1) => new LoadVarInst((Variable)opers[0]),
            ("stvar", 2) => new StoreVarInst((Variable)opers[0], opers[1]),
            ("varaddr", 1) => new VarAddrInst((Variable)opers[0]),

            ("ldfld", 1 or 2) => new LoadFieldInst((FieldDesc)opers[0], opers.ElementAtOrDefault(1)),
            ("stfld", 2) => new StoreFieldInst((FieldDesc)opers[0], null, opers[1]),
            ("stfld", 3) => new StoreFieldInst((FieldDesc)opers[0], opers[1], opers[2]),
            ("fldaddr", 1 or 2) => new FieldAddrInst((FieldDesc)opers[0], opers.ElementAtOrDefault(1)),

            ("arrlen", 1) => new ArrayLenInst(opers[0]),
            ("ldarr", 2) => new LoadArrayInst(opers[0], opers[1], resultType),
            //("starr", 3) => new StoreArrayInst(opers[0], opers[1], opers[2], /* ?? */),

            _ => null
        };
    }

#pragma warning disable format
    private static BinaryOp? GetBinaryOp(string name) => name switch {
        "add"       => BinaryOp.Add,
        "sub"       => BinaryOp.Sub,
        "mul"       => BinaryOp.Mul,
        "sdiv"      => BinaryOp.SDiv,
        "srem"      => BinaryOp.SRem,
        "udiv"      => BinaryOp.UDiv,
        "urem"      => BinaryOp.URem,
        "and"       => BinaryOp.And,
        "or"        => BinaryOp.Or,
        "xor"       => BinaryOp.Xor,
        "shl"       => BinaryOp.Shl,
        "shra"      => BinaryOp.Shra,
        "shrl"      => BinaryOp.Shrl,
        "fadd"      => BinaryOp.FAdd,
        "fsub"      => BinaryOp.FSub,
        "fmul"      => BinaryOp.FMul,
        "fdiv"      => BinaryOp.FDiv,
        "frem"      => BinaryOp.FRem,
        "add.ovf"   => BinaryOp.AddOvf,
        "sub.ovf"   => BinaryOp.SubOvf,
        "mul.ovf"   => BinaryOp.MulOvf,
        "uadd.ovf"  => BinaryOp.UAddOvf,
        "usub.ovf"  => BinaryOp.USubOvf,
        "umul.ovf"  => BinaryOp.UMulOvf,
        _ => null
    };
    private static CompareOp? GetCompareOp(string name) => name switch {
        "cmp.eq"    => CompareOp.Eq,
        "cmp.ne"    => CompareOp.Ne,
        "icmp.slt"  => CompareOp.Slt,
        "icmp.sgt"  => CompareOp.Sgt,
        "icmp.sle"  => CompareOp.Sle,
        "icmp.sge"  => CompareOp.Sge,
        "icmp.ult"  => CompareOp.Ult,
        "icmp.ugt"  => CompareOp.Ugt,
        "icmp.ule"  => CompareOp.Ule,
        "icmp.uge"  => CompareOp.Uge,

        "fcmp.olt"  => CompareOp.FOlt,
        "fcmp.ogt"  => CompareOp.FOgt,
        "fcmp.ole"  => CompareOp.FOle,
        "fcmp.oge"  => CompareOp.FOge,
        "fcmp.oeq"  => CompareOp.FOeq,
        "fcmp.one"  => CompareOp.FOne,

        "fcmp.ult"  => CompareOp.FUlt,
        "fcmp.ugt"  => CompareOp.FUgt,
        "fcmp.ule"  => CompareOp.FUle,
        "fcmp.uge"  => CompareOp.FUge,
        "fcmp.ueq"  => CompareOp.FUeq,
        "fcmp.une"  => CompareOp.FUne,
        _ => null
    };
#pragma warning restore format
}