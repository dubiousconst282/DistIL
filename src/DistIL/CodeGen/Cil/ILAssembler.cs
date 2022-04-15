namespace DistIL.CodeGen.Cil;

using System.Reflection.Metadata;

using DistIL.AsmIO;
using DistIL.IR;

public class ILAssembler
{
    ILInstruction[] _insts = new ILInstruction[16];
    int _pos;

    public Span<ILInstruction> GetInstructions()
    {
        return _insts.AsSpan(0, _pos);
    }

    /// <summary> Creates a new empty label to be marked later with <see cref="MarkLabel(Label)"/>. </summary>
    public Label DefineLabel()
    {
        return new Label();
    }
    /// <summary> Marks the label to point to the last added instruction. </summary>
    public void MarkLabel(Label lbl)
    {
        Ensure(lbl._index < 0, "Label already marked");

        //Check and remove branches like "br IL_0002; IL_0002: ..."
        if (_pos > 0 && _insts[_pos - 1].Operand == lbl) {
            _pos--;
        }
        lbl._index = _pos;
    }
    /// <summary> Creates a label that points to the next instruction. </summary>
    public Label AddLabel()
    {
        var label = DefineLabel();
        MarkLabel(label);
        return label;
    }
    public void Add(in ILInstruction inst)
    {
        if (_pos >= _insts.Length) {
            Array.Resize(ref _insts, _insts.Length * 2);
        }
        ref var dest = ref _insts[_pos++];
        dest = inst;
        //Optimize(ref dest);
    }
    public void Emit(ILCode op, object? operand = null) => Add(new(op, operand));

    public void Bake(BlobBuilder bb)
    {
        ComputeOffsets();

        foreach (ref var inst in GetInstructions()) {
            ILInstruction.Encode(bb, ref inst);
        }
    }

    private void Optimize(ref ILInstruction inst)
    {
        var code = inst.OpCode;
        switch (code) {
            case ILCode.Ldc_I4: {
                int cst = (int)inst.Operand!;
                if (cst >= -1 && cst <= 8) {
                    inst.OpCode = ILCode.Ldc_I4_0 + cst;
                    inst.Operand = null;
                } else if ((sbyte)cst == cst) {
                    inst.OpCode = ILCode.Ldc_I4_S;
                    inst.Operand = (sbyte)cst;
                }
                break;
            }
            case ILCode.Ldloc: InlineVar(ref inst, ILCode.Ldloc_0, 4, ILCode.Ldloc_S); break;
            case ILCode.Stloc: InlineVar(ref inst, ILCode.Stloc_0, 4, ILCode.Stloc_S); break;
            case ILCode.Ldarg: InlineVar(ref inst, ILCode.Ldarg_0, 4, ILCode.Ldarg_S); break;
            case ILCode.Starg: InlineVar(ref inst, ILCode.Nop, 0, ILCode.Starg_S); break;
            case ILCode.Ldloca: InlineVar(ref inst, ILCode.Nop, 0, ILCode.Ldloca_S); break;
            case ILCode.Ldarga: InlineVar(ref inst, ILCode.Nop, 0, ILCode.Ldarga_S); break;
            case ILCode.Ldelem: InlineType(ref inst, _ldelemMacros); break;
            case ILCode.Stelem: InlineType(ref inst, _stelemMacros); break;
        }

        void InlineVar(ref ILInstruction inst, ILCode inlineCode, int inlineCount, ILCode shortCode)
        {
            int n = (int)inst.Operand!;
            if (n < inlineCount) {
                inst.OpCode = inlineCode + n;
                inst.Operand = null;
            } else if (n < 256) {
                inst.OpCode = shortCode;
            }
        }
        void InlineType(ref ILInstruction inst, Dictionary<TypeKind, ILCode> macros)
        {
            if (inst.Operand is RType rt && macros.TryGetValue(rt.Kind, out var code)) {
                inst.OpCode = code;
                inst.Operand = null;
            }
        }
    }
    
    public void ComputeOffsets()
    {
        //Compute offsets
        var insts = GetInstructions();
        int offset = 0;
        foreach (ref var inst in insts) {
            inst.Offset = offset;
            offset += inst.GetSize();
        }
        //Optimize branch sizes
        //This loop should always converge, TrySimplify will either shrink code or do nothing.
        var prevOffsets = new Dictionary<int, int>();
        bool changed = true;
        while (changed) {
            changed = false;

            offset = 0;
            for (int i = 0; i < insts.Length; i++) {
                changed |= TrySimplify(insts, i);
                insts[i].Offset = offset;
                offset += insts[i].GetSize();
            }
        }
        //Replace labels with offsets
        foreach (ref var inst in insts) {
            if (inst.Operand is Label label) {
                inst.Operand = insts[label._index].Offset;
            }
            else if (inst.Operand is Label[] labels) {
                var offsets = new int[labels.Length];
                for (int i = 0; i < labels.Length; i++) {
                    offsets[i] = insts[labels[i]._index].Offset;
                }
                inst.Operand = offsets;
            }
        }

        bool TrySimplify(Span<ILInstruction> insts, int index)
        {
            ref var inst = ref insts[index];
            if (inst.Operand is Label target) {
                int delta = insts[target._index].Offset - inst.GetEndOffset();

                bool changed = prevOffsets.TryGetValue(index, out int prevOffset) && prevOffset != inst.Offset;
                prevOffsets[index] = inst.Offset;

                if (delta >= -128 && delta <= 127 && _shortBranches.TryGetValue(inst.OpCode, out var shortOp)) {
                    inst.OpCode = shortOp;
                    changed = true;
                }
                return changed;
            }
            return false;
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var inst in GetInstructions()) {
            sb.AppendLine(inst.ToString());
        }
        return sb.ToString();
    }

#pragma warning disable format
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
    private static readonly Dictionary<ILCode, ILCode> _shortBranches = new() {
        { ILCode.Br,        ILCode.Br_S },
        { ILCode.Brfalse,   ILCode.Brfalse_S },
        { ILCode.Brtrue,    ILCode.Brtrue_S },
        { ILCode.Beq,       ILCode.Beq_S },
        { ILCode.Bge,       ILCode.Bge_S },
        { ILCode.Bgt,       ILCode.Bgt_S },
        { ILCode.Ble,       ILCode.Ble_S },
        { ILCode.Blt,       ILCode.Blt_S },
        { ILCode.Bne_Un,    ILCode.Bne_Un_S },
        { ILCode.Bge_Un,    ILCode.Bge_Un_S },
        { ILCode.Bgt_Un,    ILCode.Bgt_Un_S },
        { ILCode.Ble_Un,    ILCode.Ble_Un_S },
        { ILCode.Blt_Un,    ILCode.Blt_Un_S },
    };
#pragma warning restore format
}
public class Label
{
    internal int _index = -1;

    public override string ToString() => $"LBL_{_index}";
}