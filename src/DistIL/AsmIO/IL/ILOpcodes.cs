//Based on https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Reflection/Emit/OpCodes.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace DistIL.AsmIO;

using OT = ILOperandType;
using FC = ILFlowControl;
using SB = ILStackBehaviour;

// Enum for opcode values. Note that the value names are used to construct
// publicly visible ilasm-compatible opcode names, so their exact form is important!
public enum ILCode : ushort
{
    Nop = 0x00,
    Break = 0x01,
    Ldarg_0 = 0x02,
    Ldarg_1 = 0x03,
    Ldarg_2 = 0x04,
    Ldarg_3 = 0x05,
    Ldloc_0 = 0x06,
    Ldloc_1 = 0x07,
    Ldloc_2 = 0x08,
    Ldloc_3 = 0x09,
    Stloc_0 = 0x0a,
    Stloc_1 = 0x0b,
    Stloc_2 = 0x0c,
    Stloc_3 = 0x0d,
    Ldarg_S = 0x0e,
    Ldarga_S = 0x0f,
    Starg_S = 0x10,
    Ldloc_S = 0x11,
    Ldloca_S = 0x12,
    Stloc_S = 0x13,
    Ldnull = 0x14,
    Ldc_I4_M1 = 0x15,
    Ldc_I4_0 = 0x16,
    Ldc_I4_1 = 0x17,
    Ldc_I4_2 = 0x18,
    Ldc_I4_3 = 0x19,
    Ldc_I4_4 = 0x1a,
    Ldc_I4_5 = 0x1b,
    Ldc_I4_6 = 0x1c,
    Ldc_I4_7 = 0x1d,
    Ldc_I4_8 = 0x1e,
    Ldc_I4_S = 0x1f,
    Ldc_I4 = 0x20,
    Ldc_I8 = 0x21,
    Ldc_R4 = 0x22,
    Ldc_R8 = 0x23,
    Dup = 0x25,
    Pop = 0x26,
    Jmp = 0x27,
    Call = 0x28,
    Calli = 0x29,
    Ret = 0x2a,
    Br_S = 0x2b,
    Brfalse_S = 0x2c,
    Brtrue_S = 0x2d,
    Beq_S = 0x2e,
    Bge_S = 0x2f,
    Bgt_S = 0x30,
    Ble_S = 0x31,
    Blt_S = 0x32,
    Bne_Un_S = 0x33,
    Bge_Un_S = 0x34,
    Bgt_Un_S = 0x35,
    Ble_Un_S = 0x36,
    Blt_Un_S = 0x37,
    Br = 0x38,
    Brfalse = 0x39,
    Brtrue = 0x3a,
    Beq = 0x3b,
    Bge = 0x3c,
    Bgt = 0x3d,
    Ble = 0x3e,
    Blt = 0x3f,
    Bne_Un = 0x40,
    Bge_Un = 0x41,
    Bgt_Un = 0x42,
    Ble_Un = 0x43,
    Blt_Un = 0x44,
    Switch = 0x45,
    Ldind_I1 = 0x46,
    Ldind_U1 = 0x47,
    Ldind_I2 = 0x48,
    Ldind_U2 = 0x49,
    Ldind_I4 = 0x4a,
    Ldind_U4 = 0x4b,
    Ldind_I8 = 0x4c,
    Ldind_I = 0x4d,
    Ldind_R4 = 0x4e,
    Ldind_R8 = 0x4f,
    Ldind_Ref = 0x50,
    Stind_Ref = 0x51,
    Stind_I1 = 0x52,
    Stind_I2 = 0x53,
    Stind_I4 = 0x54,
    Stind_I8 = 0x55,
    Stind_R4 = 0x56,
    Stind_R8 = 0x57,
    Add = 0x58,
    Sub = 0x59,
    Mul = 0x5a,
    Div = 0x5b,
    Div_Un = 0x5c,
    Rem = 0x5d,
    Rem_Un = 0x5e,
    And = 0x5f,
    Or = 0x60,
    Xor = 0x61,
    Shl = 0x62,
    Shr = 0x63,
    Shr_Un = 0x64,
    Neg = 0x65,
    Not = 0x66,
    Conv_I1 = 0x67,
    Conv_I2 = 0x68,
    Conv_I4 = 0x69,
    Conv_I8 = 0x6a,
    Conv_R4 = 0x6b,
    Conv_R8 = 0x6c,
    Conv_U4 = 0x6d,
    Conv_U8 = 0x6e,
    Callvirt = 0x6f,
    Cpobj = 0x70,
    Ldobj = 0x71,
    Ldstr = 0x72,
    Newobj = 0x73,
    Castclass = 0x74,
    Isinst = 0x75,
    Conv_R_Un = 0x76,
    Unbox = 0x79,
    Throw = 0x7a,
    Ldfld = 0x7b,
    Ldflda = 0x7c,
    Stfld = 0x7d,
    Ldsfld = 0x7e,
    Ldsflda = 0x7f,
    Stsfld = 0x80,
    Stobj = 0x81,
    Conv_Ovf_I1_Un = 0x82,
    Conv_Ovf_I2_Un = 0x83,
    Conv_Ovf_I4_Un = 0x84,
    Conv_Ovf_I8_Un = 0x85,
    Conv_Ovf_U1_Un = 0x86,
    Conv_Ovf_U2_Un = 0x87,
    Conv_Ovf_U4_Un = 0x88,
    Conv_Ovf_U8_Un = 0x89,
    Conv_Ovf_I_Un = 0x8a,
    Conv_Ovf_U_Un = 0x8b,
    Box = 0x8c,
    Newarr = 0x8d,
    Ldlen = 0x8e,
    Ldelema = 0x8f,
    Ldelem_I1 = 0x90,
    Ldelem_U1 = 0x91,
    Ldelem_I2 = 0x92,
    Ldelem_U2 = 0x93,
    Ldelem_I4 = 0x94,
    Ldelem_U4 = 0x95,
    Ldelem_I8 = 0x96,
    Ldelem_I = 0x97,
    Ldelem_R4 = 0x98,
    Ldelem_R8 = 0x99,
    Ldelem_Ref = 0x9a,
    Stelem_I = 0x9b,
    Stelem_I1 = 0x9c,
    Stelem_I2 = 0x9d,
    Stelem_I4 = 0x9e,
    Stelem_I8 = 0x9f,
    Stelem_R4 = 0xa0,
    Stelem_R8 = 0xa1,
    Stelem_Ref = 0xa2,
    Ldelem = 0xa3,
    Stelem = 0xa4,
    Unbox_Any = 0xa5,
    Conv_Ovf_I1 = 0xb3,
    Conv_Ovf_U1 = 0xb4,
    Conv_Ovf_I2 = 0xb5,
    Conv_Ovf_U2 = 0xb6,
    Conv_Ovf_I4 = 0xb7,
    Conv_Ovf_U4 = 0xb8,
    Conv_Ovf_I8 = 0xb9,
    Conv_Ovf_U8 = 0xba,
    Refanyval = 0xc2,
    Ckfinite = 0xc3,
    Mkrefany = 0xc6,
    Ldtoken = 0xd0,
    Conv_U2 = 0xd1,
    Conv_U1 = 0xd2,
    Conv_I = 0xd3,
    Conv_Ovf_I = 0xd4,
    Conv_Ovf_U = 0xd5,
    Add_Ovf = 0xd6,
    Add_Ovf_Un = 0xd7,
    Mul_Ovf = 0xd8,
    Mul_Ovf_Un = 0xd9,
    Sub_Ovf = 0xda,
    Sub_Ovf_Un = 0xdb,
    Endfinally = 0xdc,
    Leave = 0xdd,
    Leave_S = 0xde,
    Stind_I = 0xdf,
    Conv_U = 0xe0,
    Prefix7 = 0xf8,
    Prefix6 = 0xf9,
    Prefix5 = 0xfa,
    Prefix4 = 0xfb,
    Prefix3 = 0xfc,
    Prefix2 = 0xfd,
    Prefix1 = 0xfe,
    Prefixref = 0xff,
    Arglist = 0xfe00,
    Ceq = 0xfe01,
    Cgt = 0xfe02,
    Cgt_Un = 0xfe03,
    Clt = 0xfe04,
    Clt_Un = 0xfe05,
    Ldftn = 0xfe06,
    Ldvirtftn = 0xfe07,
    Ldarg = 0xfe09,
    Ldarga = 0xfe0a,
    Starg = 0xfe0b,
    Ldloc = 0xfe0c,
    Ldloca = 0xfe0d,
    Stloc = 0xfe0e,
    Localloc = 0xfe0f,
    Endfilter = 0xfe11,
    Unaligned_ = 0xfe12,
    Volatile_ = 0xfe13,
    Tail_ = 0xfe14,
    Initobj = 0xfe15,
    Constrained_ = 0xfe16,
    Cpblk = 0xfe17,
    Initblk = 0xfe18,
    No_ = 0xfe19,
    Rethrow = 0xfe1a,
    Sizeof = 0xfe1c,
    Refanytype = 0xfe1d,
    Readonly_ = 0xfe1e,
    // If you add more opcodes here, modify Name to handle them correctly
}

public static partial class ILCodes
{
    static ILCodes()
    {
        Reg(ILCode.Nop,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Break,
            ((int)OT.None) |
            ((int)FC.Break << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldarg_0,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldarg_1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldarg_2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldarg_3,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldloc_0,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldloc_1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldloc_2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldloc_3,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Stloc_0,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Stloc_1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Stloc_2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Stloc_3,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldarg_S,
            ((int)OT.ShortVar) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldarga_S,
            ((int)OT.ShortVar) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Starg_S,
            ((int)OT.ShortVar) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldloc_S,
            ((int)OT.ShortVar) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldloca_S,
            ((int)OT.ShortVar) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Stloc_S,
            ((int)OT.ShortVar) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldnull,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_M1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_0,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_3,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_5,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_6,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_7,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4_S,
            ((int)OT.ShortI) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I4,
            ((int)OT.I) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_I8,
            ((int)OT.I8) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_R4,
            ((int)OT.ShortR) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushr4 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldc_R8,
            ((int)OT.R) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushr8 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Dup,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push1_push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Pop,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Jmp,
            ((int)OT.Method) |
            ((int)FC.Call << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Call,
            ((int)OT.Method) |
            ((int)FC.Call << FCShift) |
            ((int)SB.Varpop << SBPopShift) |
            ((int)SB.Varpush << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Calli,
            ((int)OT.Sig) |
            ((int)FC.Call << FCShift) |
            ((int)SB.Varpop << SBPopShift) |
            ((int)SB.Varpush << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ret,
            ((int)OT.None) |
            ((int)FC.Return << FCShift) |
            ((int)SB.Varpop << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Br_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.Branch << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Brfalse_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Brtrue_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Beq_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bge_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bgt_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Ble_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Blt_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bne_Un_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bge_Un_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bgt_Un_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Ble_Un_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Blt_Un_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Br,
            ((int)OT.BrTarget) |
            ((int)FC.Branch << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Brfalse,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Brtrue,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Beq,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bge,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bgt,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Ble,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Blt,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bne_Un,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bge_Un,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Bgt_Un,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Ble_Un,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Blt_Un,
            ((int)OT.BrTarget) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Switch,
            ((int)OT.Switch) |
            ((int)FC.CondBranch << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldind_I1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_U1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_I2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_U2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_I4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_U4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_I8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_I,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_R4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushr4 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_R8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushr8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldind_Ref,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Stind_Ref,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Stind_I1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Stind_I2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Stind_I4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Stind_I8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi8 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Stind_R4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popr4 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Stind_R8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popr8 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Add,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Sub,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Mul,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Div,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Div_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Rem,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Rem_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.And,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Or,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Xor,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Shl,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Shr,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Shr_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Neg,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Not,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_I1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_I2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_I4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_I8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_R4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushr4 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_R8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushr8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_U4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_U8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Callvirt,
            ((int)OT.Method) |
            ((int)FC.Call << FCShift) |
            ((int)SB.Varpop << SBPopShift) |
            ((int)SB.Varpush << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Cpobj,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Ldobj,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldstr,
            ((int)OT.String) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Newobj,
            ((int)OT.Method) |
            ((int)FC.Call << FCShift) |
            ((int)SB.Varpop << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Castclass,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Isinst,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_R_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushr8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Unbox,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Throw,
            ((int)OT.None) |
            ((int)FC.Throw << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldfld,
            ((int)OT.Field) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldflda,
            ((int)OT.Field) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Stfld,
            ((int)OT.Field) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Ldsfld,
            ((int)OT.Field) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldsflda,
            ((int)OT.Field) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Stsfld,
            ((int)OT.Field) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Stobj,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I1_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I2_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I4_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I8_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U1_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U2_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U4_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U8_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Box,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Newarr,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldlen,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldelema,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_I1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_U1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_I2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_U2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_I4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_U4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_I8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_I,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_R4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushr4 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_R8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushr8 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldelem_Ref,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Pushref << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Stelem_I,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_I1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_I2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_I4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_I8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popi8 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_R4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popr4 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_R8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popr8 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Stelem_Ref,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_popref << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Ldelem,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Stelem,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref_popi_pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Unbox_Any,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U4,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U8,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Refanyval,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ckfinite,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushr8 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Mkrefany,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldtoken,
            ((int)OT.Tok) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Conv_U2,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_U1,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_I,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_I,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Conv_Ovf_U,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Add_Ovf,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Add_Ovf_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Mul_Ovf,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Mul_Ovf_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Sub_Ovf,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Sub_Ovf_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Endfinally,
            ((int)OT.None) |
            ((int)FC.Return << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Leave,
            ((int)OT.BrTarget) |
            ((int)FC.Branch << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Leave_S,
            ((int)OT.ShortBrTarget) |
            ((int)FC.Branch << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Stind_I,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-2 << StackChangeShift)
        );
        Reg(ILCode.Conv_U,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix7,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix6,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix5,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix4,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix3,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix2,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefix1,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Prefixref,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Arglist,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ceq,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Cgt,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Cgt_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Clt,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Clt_Un,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1_pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldftn,
            ((int)OT.Method) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldvirtftn,
            ((int)OT.Method) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popref << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Ldarg,
            ((int)OT.Var) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldarga,
            ((int)OT.Var) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Starg,
            ((int)OT.Var) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Ldloc,
            ((int)OT.Var) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push1 << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Ldloca,
            ((int)OT.Var) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Stloc,
            ((int)OT.Var) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Localloc,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Endfilter,
            ((int)OT.None) |
            ((int)FC.Return << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Unaligned_,
            ((int)OT.ShortI) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Volatile_,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Tail_,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Initobj,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-1 << StackChangeShift)
        );
        Reg(ILCode.Constrained_,
            ((int)OT.Type) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Cpblk,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.Initblk,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Popi_popi_popi << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (-3 << StackChangeShift)
        );
        Reg(ILCode.No_,
            ((int)OT.ShortI) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Rethrow,
            ((int)OT.None) |
            ((int)FC.Throw << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            EndsUncondJmpBlkFlag |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Sizeof,
            ((int)OT.Type) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (1 << StackChangeShift)
        );
        Reg(ILCode.Refanytype,
            ((int)OT.None) |
            ((int)FC.Next << FCShift) |
            ((int)SB.Pop1 << SBPopShift) |
            ((int)SB.Pushi << SBPushShift) |
            (0 << StackChangeShift)
        );
        Reg(ILCode.Readonly_,
            ((int)OT.None) |
            ((int)FC.Meta << FCShift) |
            ((int)SB.Pop0 << SBPopShift) |
            ((int)SB.Push0 << SBPushShift) |
            (0 << StackChangeShift)
        );
    }
}