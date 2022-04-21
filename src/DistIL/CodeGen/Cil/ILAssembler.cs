namespace DistIL.CodeGen.Cil;

using System.Reflection.Metadata;

using DistIL.AsmIO;
using DistIL.IR;

public class ILAssembler
{
    ILInstruction[] _insts = new ILInstruction[16];
    int _pos;

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
        while (_pos > 0 && _insts[_pos - 1].Operand == lbl) {
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
    public void Emit(ILCode op, object? operand = null)
    {
        if (_pos >= _insts.Length) {
            Array.Resize(ref _insts, _insts.Length * 2);
        }
        _insts[_pos++] = new ILInstruction(op, operand);
    }

    public ArraySegment<ILInstruction> Bake()
    {
        ComputeOffsets();
        return new(_insts, 0, _pos);
    }

    private void ComputeOffsets()
    {
        //Compute offsets
        var insts = _insts.AsSpan(0, _pos);
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
        foreach (var inst in _insts.AsSpan(0, _pos)) {
            sb.AppendLine(inst.ToString());
        }
        return sb.ToString();
    }

#pragma warning disable format
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