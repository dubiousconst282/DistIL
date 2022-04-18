namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

public struct ILInstruction
{
    public ILCode OpCode { get; set; }
    public int Offset { get; set; }
    /// <summary> Operand value, one of `null, int, long, float, double, Handle, or int[]`. </summary>
    public object? Operand { get; set; }
    //We could probably use an extra long field to store primitive operands and avoid allocs, 
    //but the extra complexity may not be worth it.

    public ILOperandType OperandType => OpCode.GetOperandType();
    public ILFlowControl FlowControl => OpCode.GetFlowControl();

    public ILInstruction(ILCode op, object? operand = null)
    {
        OpCode = op;
        Offset = 0;
        Operand = operand;
    }

    public static ILInstruction Decode(ref SpanReader reader)
    {
        int baseOffset = reader.Offset;
        int code = reader.ReadByte();
        if (code == 0xFE) {
            code = (code << 8) | reader.ReadByte();
        }
        var opcode = (ILCode)code;
        object? operand = opcode.GetOperandType() switch {
            ILOperandType.BrTarget => reader.ReadLE<int>() + reader.Offset,
            ILOperandType.Field or
            ILOperandType.Method or
            ILOperandType.Sig or
            ILOperandType.String or
            ILOperandType.Tok or
            ILOperandType.Type
                => MetadataTokens.Handle(reader.ReadLE<int>()),
            ILOperandType.I => reader.ReadLE<int>(),
            ILOperandType.I8 => reader.ReadLE<long>(),
            ILOperandType.R => reader.ReadLE<double>(),
            ILOperandType.Switch => ReadJumpTable(ref reader),
            ILOperandType.Var => (int)reader.ReadLE<ushort>(),
            ILOperandType.ShortBrTarget => (int)reader.ReadLE<sbyte>() + reader.Offset,
            ILOperandType.ShortI => (int)reader.ReadLE<sbyte>(),
            ILOperandType.ShortR => reader.ReadLE<float>(),
            ILOperandType.ShortVar => (int)reader.ReadByte(),
            _ => null
        };
        return new ILInstruction() {
            OpCode = opcode,
            Offset = baseOffset,
            Operand = operand
        };

        static int[] ReadJumpTable(ref SpanReader reader)
        {
            int count = reader.ReadLE<int>();
            int baseOffset = reader.Offset + count * 4;
            var targets = new int[count];

            for (int i = 0; i < count; i++) {
                targets[i] = baseOffset + reader.ReadLE<int>();
            }
            return targets;
        }
    }
    public static void Encode(BlobBuilder bb, ref ILInstruction inst)
    {
        int code = (int)inst.OpCode;
        if ((code & 0xFF00) == 0xFE00) {
            bb.WriteByte((byte)(code >> 8));
        }
        bb.WriteByte((byte)code);

        switch (inst.OpCode.GetOperandType()) {
            case ILOperandType.BrTarget: {
                bb.WriteInt32((int)inst.Operand! - inst.GetEndOffset());
                break;
            }
            case ILOperandType.Field:
            case ILOperandType.Method:
            case ILOperandType.Sig:
            case ILOperandType.String:
            case ILOperandType.Tok:
            case ILOperandType.Type: {
                bb.WriteInt32(MetadataTokens.GetToken((Handle)inst.Operand!));
                break;
            }
            case ILOperandType.I: {
                bb.WriteInt32((int)inst.Operand!);
                break;
            }
            case ILOperandType.I8: {
                bb.WriteInt64((long)inst.Operand!);
                break;
            }
            case ILOperandType.R: {
                bb.WriteDouble((double)inst.Operand!);
                break;
            }
            case ILOperandType.Switch: {
                WriteTumpTable(bb, ref inst);
                break;
            }
            case ILOperandType.Var: {
                int varIndex = (int)inst.Operand!;
                Assert(varIndex == (ushort)varIndex);
                bb.WriteUInt16((ushort)varIndex);
                break;
            }
            case ILOperandType.ShortBrTarget: {
                int offset = (int)inst.Operand! - inst.GetEndOffset();
                Assert(offset == (sbyte)offset);
                bb.WriteSByte((sbyte)offset);
                break;
            }
            case ILOperandType.ShortI: {
                int value = (int)inst.Operand!;
                Assert(value == (sbyte)value);
                bb.WriteSByte((sbyte)value);
                break;
            }
            case ILOperandType.ShortR: {
                bb.WriteSingle((float)inst.Operand!);
                break;
            }
            case ILOperandType.ShortVar: {
                int varIndex = (int)inst.Operand!;
                Assert(varIndex == (byte)varIndex);
                bb.WriteByte((byte)varIndex);
                break;
            }
        }
        static void WriteTumpTable(BlobBuilder bb, ref ILInstruction inst)
        {
            int baseOffset = inst.GetEndOffset();
            var targets = (int[])inst.Operand!;

            bb.WriteInt32(targets.Length);
            for (int i = 0; i < targets.Length; i++) {
                bb.WriteInt32(targets[i] - baseOffset);
            }
        }
    }

    public int GetSize()
    {
        int operandSize = OperandType switch {
            ILOperandType.None
                => 0,
            ILOperandType.ShortBrTarget or
            ILOperandType.ShortI or
            ILOperandType.ShortVar
                => 1,
            ILOperandType.Var
                => 2,
            ILOperandType.BrTarget or
            ILOperandType.Field or
            ILOperandType.Method or
            ILOperandType.Sig or
            ILOperandType.String or
            ILOperandType.Tok or
            ILOperandType.Type or
            ILOperandType.I or
            ILOperandType.ShortR
                => 4,
            ILOperandType.I8 or
            ILOperandType.R
                => 8,
            ILOperandType.Switch
                => 4 + ((Array)Operand!).Length * 4,
            _ => throw new InvalidOperationException()
        };
        return OpCode.GetSize() + operandSize;
    }
    public int GetEndOffset()
    {
        return Offset + GetSize();
    }

    public override string ToString()
    {
        string? operandStr = OpCode.GetOperandType() switch {
            ILOperandType.None => null,
            ILOperandType.BrTarget or
            ILOperandType.ShortBrTarget
                when Operand is int targetOffset
                => $"IL_{targetOffset:X4}",
            _ when Operand is Handle hnd 
                => $"0x{MetadataTokens.GetToken(hnd):X8} /* {hnd.Kind} */",
            _ => Operand?.ToString()
        };
        return $"IL_{Offset:X4}: {OpCode.GetName()}{(operandStr == null ? "" : " ")}{operandStr}";
    }
}
