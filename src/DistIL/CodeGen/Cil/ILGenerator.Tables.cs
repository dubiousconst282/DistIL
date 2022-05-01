namespace DistIL.CodeGen.Cil;

using DistIL.AsmIO;
using DistIL.IR;

public partial class ILGenerator
{
#pragma warning disable format
    const bool T = true, F = false;

    private static ILCode GetCodeForBinOp(BinaryOp op)
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

    private static ILCode GetCodeForUnOp(UnaryOp op)
    {
        return op switch {
            UnaryOp.FNeg or UnaryOp.Neg => ILCode.Neg,
            UnaryOp.Not => ILCode.Not,
            _ => throw new InvalidOperationException()
        };
    }

    private static ILCode GetCodeForConv(ConvertInst inst)
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

    private static bool GetCodeForBranch(CompareOp op, out ILCode code, out bool invert)
    {
        (code, invert) = op switch {
            CompareOp.Eq  or CompareOp.FOeq => (ILCode.Beq,     F),
            CompareOp.Ne  or CompareOp.FUne => (ILCode.Bne_Un,  F),
            CompareOp.Slt or CompareOp.FOlt => (ILCode.Blt,     F),
            CompareOp.Sgt or CompareOp.FOgt => (ILCode.Bgt,     F),
            CompareOp.Sle or CompareOp.FOle => (ILCode.Ble,     F),
            CompareOp.Sge or CompareOp.FOge => (ILCode.Bge,     F),
            CompareOp.Ult or CompareOp.FUlt => (ILCode.Blt_Un,  F),
            CompareOp.Ugt or CompareOp.FUgt => (ILCode.Bgt_Un,  F),
            CompareOp.Ule or CompareOp.FUle => (ILCode.Ble_Un,  F),
            CompareOp.Uge or CompareOp.FUge => (ILCode.Bge_Un,  F),
            //TODO: FUeq/FOne
            _ => (ILCode.Nop, F)
        };
        return code != ILCode.Nop;
    }

    private static (ILCode Code, bool Invert) GetCodeForCompare(CompareOp op)
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

    private static (ILCode Ld, ILCode St) GetCodeForPtrAcc(RType type)
    {
        return type.Kind switch {
            TypeKind.Bool or TypeKind.Byte      => (ILCode.Ldind_U1, ILCode.Stind_I1),
            TypeKind.SByte                      => (ILCode.Ldind_I1, ILCode.Stind_I1),
            TypeKind.Char or TypeKind.UInt16    => (ILCode.Ldind_U2, ILCode.Stind_I2),
            TypeKind.Int16                      => (ILCode.Ldind_I2, ILCode.Stind_I2),
            TypeKind.Int32                      => (ILCode.Ldind_I4, ILCode.Stind_I4),
            TypeKind.UInt32                     => (ILCode.Ldind_U4, ILCode.Stind_I4),
            TypeKind.Int64 or TypeKind.UInt64   => (ILCode.Ldind_I8, ILCode.Stind_I8),
            TypeKind.Single                     => (ILCode.Ldind_R4, ILCode.Stind_R4),
            TypeKind.Double                     => (ILCode.Ldind_R8, ILCode.Stind_R8),
            TypeKind.IntPtr or TypeKind.UIntPtr => (ILCode.Ldind_I, ILCode.Stind_I),
            _                                   => (ILCode.Ldobj, ILCode.Stobj)
        };
    }

    enum VarOp { Load, Store, Addr }

    private static (ILCode Norm, ILCode Inline, ILCode Short) GetCodesForVar(VarOp op, bool isArg)
    {
        return (op, isArg) switch {
            (VarOp.Load,    F) => (ILCode.Ldloc,  ILCode.Ldloc_0, ILCode.Ldloc_S),
            (VarOp.Store,   F) => (ILCode.Stloc,  ILCode.Stloc_0, ILCode.Stloc_S),
            (VarOp.Addr,    F) => (ILCode.Ldloca, ILCode.Nop,     ILCode.Ldloca_S),
            (VarOp.Load,    T) => (ILCode.Ldarg,  ILCode.Ldarg_0, ILCode.Ldarg_S),
            (VarOp.Store,   T) => (ILCode.Starg,  ILCode.Nop,     ILCode.Starg_S),
            (VarOp.Addr,    T) => (ILCode.Ldarga, ILCode.Nop,     ILCode.Ldarga_S),
            _ => throw new InvalidOperationException()
        };
    }

    private static readonly Dictionary<TypeKind, ILCode> _ldelemMacros = new() {
        { TypeKind.Bool,    ILCode.Ldelem_U1 },
        { TypeKind.Char,    ILCode.Ldelem_U2 },
        { TypeKind.SByte,   ILCode.Ldelem_I1 },
        { TypeKind.Int16,   ILCode.Ldelem_I2 },
        { TypeKind.Int32,   ILCode.Ldelem_I4 },
        { TypeKind.Int64,   ILCode.Ldelem_I8 },
        { TypeKind.Byte,    ILCode.Ldelem_U1 },
        { TypeKind.UInt16,  ILCode.Ldelem_U2 },
        { TypeKind.UInt32,  ILCode.Ldelem_U4 },
        { TypeKind.UInt64,  ILCode.Ldelem_I8 },
        { TypeKind.Single,  ILCode.Ldelem_R4 },
        { TypeKind.Double,  ILCode.Ldelem_R8 },
        { TypeKind.IntPtr,  ILCode.Ldelem_I },
        { TypeKind.UIntPtr, ILCode.Ldelem_I },
        { TypeKind.Pointer, ILCode.Ldelem_I },

        { TypeKind.Object,  ILCode.Ldelem_Ref },
        { TypeKind.String,  ILCode.Ldelem_Ref },
    };
    private static readonly Dictionary<TypeKind, ILCode> _stelemMacros = new() {
        { TypeKind.Bool,    ILCode.Stelem_I1 },
        { TypeKind.Char,    ILCode.Stelem_I2 },
        { TypeKind.SByte,   ILCode.Stelem_I1 },
        { TypeKind.Int16,   ILCode.Stelem_I2 },
        { TypeKind.Int32,   ILCode.Stelem_I4 },
        { TypeKind.Int64,   ILCode.Stelem_I8 },
        { TypeKind.Byte,    ILCode.Stelem_I1 },
        { TypeKind.UInt16,  ILCode.Stelem_I2 },
        { TypeKind.UInt32,  ILCode.Stelem_I4 },
        { TypeKind.UInt64,  ILCode.Stelem_I8 },
        { TypeKind.Single,  ILCode.Stelem_R4 },
        { TypeKind.Double,  ILCode.Stelem_R8 },
        { TypeKind.IntPtr,  ILCode.Stelem_I },
        { TypeKind.UIntPtr, ILCode.Stelem_I },
        { TypeKind.Pointer, ILCode.Stelem_I },

        { TypeKind.Object,  ILCode.Stelem_Ref },
        { TypeKind.String,  ILCode.Stelem_Ref },
    };
#pragma warning restore format
}