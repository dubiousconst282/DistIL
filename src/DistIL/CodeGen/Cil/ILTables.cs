namespace DistIL.CodeGen.Cil;

using DistIL.AsmIO;
using DistIL.IR;

internal static class ILTables
{
#pragma warning disable format
    const bool T = true, F = false;

    public static ILCode GetBinaryCode(BinaryOp op)
    {
        return op switch {
            BinaryOp.Add  or BinaryOp.FAdd  => ILCode.Add,
            BinaryOp.Sub  or BinaryOp.FSub  => ILCode.Sub,
            BinaryOp.Mul  or BinaryOp.FMul  => ILCode.Mul,
            BinaryOp.SDiv or BinaryOp.FDiv  => ILCode.Div,
            BinaryOp.SRem or BinaryOp.FRem  => ILCode.Rem,
            BinaryOp.UDiv                   => ILCode.Div_Un,
            BinaryOp.URem                   => ILCode.Rem_Un,
            BinaryOp.And                    => ILCode.And,
            BinaryOp.Or                     => ILCode.Or,
            BinaryOp.Xor                    => ILCode.Xor,
            BinaryOp.Shl                    => ILCode.Shl,
            BinaryOp.Shra                   => ILCode.Shr,
            BinaryOp.Shrl                   => ILCode.Shr_Un,
            BinaryOp.AddOvf                 => ILCode.Add_Ovf,
            BinaryOp.SubOvf                 => ILCode.Sub_Ovf,
            BinaryOp.MulOvf                 => ILCode.Mul_Ovf,
            BinaryOp.UAddOvf                => ILCode.Add_Ovf_Un,
            BinaryOp.USubOvf                => ILCode.Sub_Ovf_Un,
            BinaryOp.UMulOvf                => ILCode.Mul_Ovf_Un,
            _ => throw new InvalidOperationException()
        };
    }

    public static ILCode GetUnaryCode(UnaryOp op)
    {
        return op switch {
            UnaryOp.FNeg or UnaryOp.Neg => ILCode.Neg,
            UnaryOp.Not => ILCode.Not,
            _ => throw new InvalidOperationException()
        };
    }

    public static ILCode GetConvertCode(ConvertInst inst)
    {
        var srcType = inst.Value.ResultType;
        var dstType = inst.ResultType;

        return (dstType.Kind, inst.SrcUnsigned, inst.CheckOverflow) switch {
            (TypeKind.SByte,   F, F) => ILCode.Conv_I1,
            (TypeKind.Int16,   F, F) => ILCode.Conv_I2,
            (TypeKind.Int32,   F, F) => ILCode.Conv_I4,
            (TypeKind.Int64,   F, F) => ILCode.Conv_I8,
            (TypeKind.Byte,    F, F) => ILCode.Conv_U1,
            (TypeKind.UInt16,  F, F) => ILCode.Conv_U2,
            (TypeKind.UInt32,  F, F) => ILCode.Conv_U4,
            (TypeKind.UInt64,  F, F) => ILCode.Conv_U8,
            (TypeKind.IntPtr,  F, F) => ILCode.Conv_I,
            (TypeKind.UIntPtr, F, F) => ILCode.Conv_U,
            (TypeKind.Single,  F, F) => ILCode.Conv_R4,
            (TypeKind.Double,  F, F) => ILCode.Conv_R8,
            
            (TypeKind.SByte,   F, T) => ILCode.Conv_Ovf_I1,
            (TypeKind.Int16,   F, T) => ILCode.Conv_Ovf_I2,
            (TypeKind.Int32,   F, T) => ILCode.Conv_Ovf_I4,
            (TypeKind.Int64,   F, T) => ILCode.Conv_Ovf_I8,
            (TypeKind.Byte,    F, T) => ILCode.Conv_Ovf_U1,
            (TypeKind.UInt16,  F, T) => ILCode.Conv_Ovf_U2,
            (TypeKind.UInt32,  F, T) => ILCode.Conv_Ovf_U4,
            (TypeKind.UInt64,  F, T) => ILCode.Conv_Ovf_U8,
            (TypeKind.IntPtr,  F, T) => ILCode.Conv_Ovf_I,
            (TypeKind.UIntPtr, F, T) => ILCode.Conv_Ovf_U,
            
            (TypeKind.SByte,   T, T) => ILCode.Conv_Ovf_I1_Un,
            (TypeKind.Int16,   T, T) => ILCode.Conv_Ovf_I2_Un,
            (TypeKind.Int32,   T, T) => ILCode.Conv_Ovf_I4_Un,
            (TypeKind.Int64,   T, T) => ILCode.Conv_Ovf_I8_Un,
            (TypeKind.Byte,    T, T) => ILCode.Conv_Ovf_U1_Un,
            (TypeKind.UInt16,  T, T) => ILCode.Conv_Ovf_U2_Un,
            (TypeKind.UInt32,  T, T) => ILCode.Conv_Ovf_U4_Un,
            (TypeKind.UInt64,  T, T) => ILCode.Conv_Ovf_U8_Un,
            (TypeKind.IntPtr,  T, T) => ILCode.Conv_Ovf_I_Un,
            (TypeKind.UIntPtr, T, T) => ILCode.Conv_Ovf_U_Un,

            (TypeKind.Single,  T, F) => ILCode.Conv_R_Un,
            (TypeKind.Double,  T, F) => ILCode.Conv_R_Un,

            _ => throw new InvalidOperationException()
        };
    }

    public static ILCode GetBranchCode(CompareOp op)
    {
        return op switch {
            CompareOp.Eq  or CompareOp.FOeq => ILCode.Beq,
            CompareOp.Ne  or CompareOp.FUne => ILCode.Bne_Un,
            CompareOp.Slt or CompareOp.FOlt => ILCode.Blt,
            CompareOp.Sgt or CompareOp.FOgt => ILCode.Bgt,
            CompareOp.Sle or CompareOp.FOle => ILCode.Ble,
            CompareOp.Sge or CompareOp.FOge => ILCode.Bge,
            CompareOp.Ult or CompareOp.FUlt => ILCode.Blt_Un,
            CompareOp.Ugt or CompareOp.FUgt => ILCode.Bgt_Un,
            CompareOp.Ule or CompareOp.FUle => ILCode.Ble_Un,
            CompareOp.Uge or CompareOp.FUge => ILCode.Bge_Un,
            _ => ILCode.Nop
        };
    }

    public static (ILCode Code, bool Invert) GetCompareCode(CompareOp op)
    {
        return op switch {
            CompareOp.Eq    => (ILCode.Ceq,     F),
            CompareOp.Ne    => (ILCode.Ceq,     T),
            CompareOp.Slt   => (ILCode.Clt,     F),
            CompareOp.Sgt   => (ILCode.Cgt,     F),
            CompareOp.Sle   => (ILCode.Cgt,     T), //x <= y  ->  !(x > y)
            CompareOp.Sge   => (ILCode.Clt,     T), //x >= y  ->  !(x < y)
            CompareOp.Ult   => (ILCode.Clt_Un,  F),
            CompareOp.Ugt   => (ILCode.Cgt_Un,  F),
            CompareOp.Ule   => (ILCode.Cgt_Un,  T), //x <= y  ->  !(x > y)
            CompareOp.Uge   => (ILCode.Clt_Un,  T), //x >= y  ->  !(x < y)

            CompareOp.FOeq  => (ILCode.Ceq,     F),
            CompareOp.FOlt  => (ILCode.Clt,     F),
            CompareOp.FOgt  => (ILCode.Cgt,     F),
            CompareOp.FOle  => (ILCode.Cgt_Un,  T),
            CompareOp.FOge  => (ILCode.Clt_Un,  T),

            CompareOp.FUne  => (ILCode.Ceq,     T),
            CompareOp.FUlt  => (ILCode.Clt_Un,  F),
            CompareOp.FUgt  => (ILCode.Cgt_Un,  F),
            CompareOp.FUle  => (ILCode.Cgt,     T),
            CompareOp.FUge  => (ILCode.Clt,     T),
            //FIXME: mappings for fcmp.one and fcmp.ueq
            //one -> x < y || x > y
            //ueq -> !one
            _ => throw new InvalidOperationException()
        };
    }

    public static readonly (ILCode Normal, ILCode Inline, ILCode Short)[] VarCodes = {
        /* Load  Var */ (ILCode.Ldloc,  ILCode.Ldloc_0, ILCode.Ldloc_S),
        /* Store Var */ (ILCode.Stloc,  ILCode.Stloc_0, ILCode.Stloc_S),
        /* Addr  Var */ (ILCode.Ldloca, ILCode.Nop,     ILCode.Ldloca_S),
        /* Load  Arg */ (ILCode.Ldarg,  ILCode.Ldarg_0, ILCode.Ldarg_S),
        /* Store Arg */ (ILCode.Starg,  ILCode.Nop,     ILCode.Starg_S),
        /* Addr  Arg */ (ILCode.Ldarga, ILCode.Nop,     ILCode.Ldarga_S),
    };
    
    public static ILCode GetShortBranchCode(ILCode code) => code switch {
        ILCode.Br      => ILCode.Br_S,
        ILCode.Brfalse => ILCode.Brfalse_S,
        ILCode.Brtrue  => ILCode.Brtrue_S,
        ILCode.Beq     => ILCode.Beq_S,
        ILCode.Bge     => ILCode.Bge_S,
        ILCode.Bgt     => ILCode.Bgt_S,
        ILCode.Ble     => ILCode.Ble_S,
        ILCode.Blt     => ILCode.Blt_S,
        ILCode.Bne_Un  => ILCode.Bne_Un_S,
        ILCode.Bge_Un  => ILCode.Bge_Un_S,
        ILCode.Bgt_Un  => ILCode.Bgt_Un_S,
        ILCode.Ble_Un  => ILCode.Ble_Un_S,
        ILCode.Blt_Un  => ILCode.Blt_Un_S,
        ILCode.Leave   => ILCode.Leave_S,
        _ => default
    };
    public static ILCode GetLongBranchCode(ILCode code) => code switch {
        ILCode.Br_S      => ILCode.Br,
        ILCode.Brfalse_S => ILCode.Brfalse,
        ILCode.Brtrue_S  => ILCode.Brtrue,
        ILCode.Beq_S     => ILCode.Beq,
        ILCode.Bge_S     => ILCode.Bge,
        ILCode.Bgt_S     => ILCode.Bgt,
        ILCode.Ble_S     => ILCode.Ble,
        ILCode.Blt_S     => ILCode.Blt,
        ILCode.Bne_Un_S  => ILCode.Bne_Un,
        ILCode.Bge_Un_S  => ILCode.Bge_Un,
        ILCode.Bgt_Un_S  => ILCode.Bgt_Un,
        ILCode.Ble_Un_S  => ILCode.Ble_Un,
        ILCode.Blt_Un_S  => ILCode.Blt_Un,
        ILCode.Leave_S   => ILCode.Leave,
        _ => default
    };

    public static ILCode GetPtrAccessCode(TypeDesc type, bool ld)
    {
        return type.Kind switch {
            TypeKind.Bool or TypeKind.Byte      => ld ? ILCode.Ldind_U1 : ILCode.Stind_I1,
            TypeKind.SByte                      => ld ? ILCode.Ldind_I1 : ILCode.Stind_I1,
            TypeKind.Char or TypeKind.UInt16    => ld ? ILCode.Ldind_U2 : ILCode.Stind_I2,
            TypeKind.Int16                      => ld ? ILCode.Ldind_I2 : ILCode.Stind_I2,
            TypeKind.Int32                      => ld ? ILCode.Ldind_I4 : ILCode.Stind_I4,
            TypeKind.UInt32                     => ld ? ILCode.Ldind_U4 : ILCode.Stind_I4,
            TypeKind.Int64 or TypeKind.UInt64   => ld ? ILCode.Ldind_I8 : ILCode.Stind_I8,
            TypeKind.Single                     => ld ? ILCode.Ldind_R4 : ILCode.Stind_R4,
            TypeKind.Double                     => ld ? ILCode.Ldind_R8 : ILCode.Stind_R8,
            TypeKind.IntPtr or TypeKind.UIntPtr => ld ? ILCode.Ldind_I : ILCode.Stind_I,
            _                                   => ld ? ILCode.Ldobj : ILCode.Stobj
        };
    }

    public static ILCode GetArrayElemMacro(TypeDesc type, bool ld) => type.Kind switch {
        TypeKind.Bool    => ld ? ILCode.Ldelem_U1 : ILCode.Stelem_I1,
        TypeKind.Char    => ld ? ILCode.Ldelem_U2 : ILCode.Stelem_I2,
        TypeKind.SByte   => ld ? ILCode.Ldelem_I1 : ILCode.Stelem_I1,
        TypeKind.Int16   => ld ? ILCode.Ldelem_I2 : ILCode.Stelem_I2,
        TypeKind.Int32   => ld ? ILCode.Ldelem_I4 : ILCode.Stelem_I4,
        TypeKind.Int64   => ld ? ILCode.Ldelem_I8 : ILCode.Stelem_I8,
        TypeKind.Byte    => ld ? ILCode.Ldelem_U1 : ILCode.Stelem_I1,
        TypeKind.UInt16  => ld ? ILCode.Ldelem_U2 : ILCode.Stelem_I2,
        TypeKind.UInt32  => ld ? ILCode.Ldelem_U4 : ILCode.Stelem_I4,
        TypeKind.UInt64  => ld ? ILCode.Ldelem_I8 : ILCode.Stelem_I8,
        TypeKind.Single  => ld ? ILCode.Ldelem_R4 : ILCode.Stelem_R4,
        TypeKind.Double  => ld ? ILCode.Ldelem_R8 : ILCode.Stelem_R8,
        TypeKind.IntPtr  => ld ? ILCode.Ldelem_I  : ILCode.Stelem_I1,
        TypeKind.UIntPtr => ld ? ILCode.Ldelem_I  : ILCode.Stelem_I1,
        TypeKind.Pointer => ld ? ILCode.Ldelem_I  : ILCode.Stelem_I1,
        _ => default
    };
#pragma warning restore format
}