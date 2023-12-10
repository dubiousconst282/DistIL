namespace DistIL.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

partial class ModuleWriter
{
    private int EmitMethodBodyRVA(ILMethodBody? body)
    {
        if (body == null) {
            return -1;
        }
        var enc = _bodyEncoder.AddMethodBody(
            codeSize: body.Instructions[^1].GetEndOffset(),
            body.MaxStack,
            body.ExceptionClauses.Length,
            hasSmallExceptionRegions: false, // TODO
            localVariablesSignature: EncodeLocalVars(body.Locals),
            attributes: body.InitLocals ? MethodBodyAttributes.InitLocals : 0
        );
        EncodeInsts(body, new BlobWriter(enc.Instructions));

        // Add exception regions
        foreach (var ehr in body.ExceptionClauses) {
            enc.ExceptionRegions.Add(
                kind: ehr.Kind,
                tryOffset: ehr.TryStart,
                tryLength: ehr.TryEnd - ehr.TryStart,
                handlerOffset: ehr.HandlerStart,
                handlerLength: ehr.HandlerEnd - ehr.HandlerStart,
                catchType: ehr.CatchType == null ? default : GetHandle(ehr.CatchType),
                filterOffset: ehr.FilterStart
            );
        }
        return enc.Offset;
    }

    private StandaloneSignatureHandle EncodeLocalVars(ILVariable[] localVars)
    {
        if (localVars.Length == 0) {
            return default;
        }
        var sigBlob = EncodeSig(b => {
            var sigEnc = b.LocalVariableSignature(localVars.Length);

            foreach (var local in localVars) {
                var typeEnc = sigEnc.AddVariable().Type(false, local.IsPinned);
                EncodeType(typeEnc, local.Type);
            }
        });
        return _builder.AddStandaloneSignature(sigBlob);
    }

    private void EncodeInsts(ILMethodBody body, BlobWriter writer)
    {
        foreach (ref var inst in body.Instructions.AsSpan()) {
            EncodeInst(ref writer, ref inst);
        }
    }

    private void EncodeInst(ref BlobWriter bw, ref ILInstruction inst)
    {
        int code = (int)inst.OpCode;
        if ((code & 0xFF00) == 0xFE00) {
            bw.WriteByte((byte)(code >> 8));
        }
        bw.WriteByte((byte)code);

        switch (inst.OpCode.GetOperandType()) {
            case ILOperandType.BrTarget: {
                bw.WriteInt32((int)inst.Operand! - inst.GetEndOffset());
                break;
            }
            case ILOperandType.Type:
            case ILOperandType.Field:
            case ILOperandType.Method:
            case ILOperandType.Tok: {
                var handle = GetHandle((EntityDesc)inst.Operand!);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.Sig: {
                var fnType = (FuncPtrType)inst.Operand!;
                var sigBlob = EncodeSig(b => EncodeMethodSig(b, fnType.Signature));
                var handle = _builder.AddStandaloneSignature(sigBlob);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.String: {
                var handle = _builder.GetOrAddUserString((string)inst.Operand!);
                bw.WriteInt32(MetadataTokens.GetToken(handle));
                break;
            }
            case ILOperandType.I: {
                bw.WriteInt32((int)inst.Operand!);
                break;
            }
            case ILOperandType.I8: {
                bw.WriteInt64((long)inst.Operand!);
                break;
            }
            case ILOperandType.R: {
                bw.WriteDouble((double)inst.Operand!);
                break;
            }
            case ILOperandType.Switch: {
                WriteTumpTable(ref bw, ref inst);
                break;
            }
            case ILOperandType.Var: {
                int varIndex = (int)inst.Operand!;
                Debug.Assert(varIndex == (ushort)varIndex);
                bw.WriteUInt16((ushort)varIndex);
                break;
            }
            case ILOperandType.ShortBrTarget: {
                int offset = (int)inst.Operand! - inst.GetEndOffset();
                Debug.Assert(offset == (sbyte)offset);
                bw.WriteSByte((sbyte)offset);
                break;
            }
            case ILOperandType.ShortI: {
                int value = (int)inst.Operand!;
                Debug.Assert(value == (sbyte)value);
                bw.WriteSByte((sbyte)value);
                break;
            }
            case ILOperandType.ShortR: {
                bw.WriteSingle((float)inst.Operand!);
                break;
            }
            case ILOperandType.ShortVar: {
                int varIndex = (int)inst.Operand!;
                Debug.Assert(varIndex == (byte)varIndex);
                bw.WriteByte((byte)varIndex);
                break;
            }
            default: {
                Debug.Assert(inst.Operand == null);
                break;
            }
        }
        static void WriteTumpTable(ref BlobWriter bw, ref ILInstruction inst)
        {
            int baseOffset = inst.GetEndOffset();
            var targets = (int[])inst.Operand!;

            bw.WriteInt32(targets.Length);
            for (int i = 0; i < targets.Length; i++) {
                bw.WriteInt32(targets[i] - baseOffset);
            }
        }
    }
}