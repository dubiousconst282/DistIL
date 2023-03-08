﻿namespace DistIL.CodeGen.Cil;

using EHRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

/// <summary> Helper for building a list of <see cref="ILInstruction"/>s. </summary>
internal class ILAssembler
{
    ILInstruction[] _insts = new ILInstruction[16];
    int _index = 0;

    Dictionary<Variable, int> _varSlots = new();
    Dictionary<ILLabel, int> _labelStarts = new();
    int _stackDepth = 0, _maxStackDepth = 0;

    /// <summary> Marks the label for the specified block. </summary>
    public void StartBlock(BasicBlock block, bool isCatchEntry)
    {
        _labelStarts.Add(block, _index);

        if (isCatchEntry) {
            _stackDepth++;
            _maxStackDepth = Math.Max(_maxStackDepth, _stackDepth);
        }
    }

    public ILLabel DefineLabel()
    {
        var label = new ILLabel(_labelStarts.Count);
        _labelStarts.Add(label, -1);
        return label;
    }
    public void MarkLabel(ILLabel label)
    {
        Debug.Assert(_labelStarts[label] < 0, "Label already marked");
        _labelStarts[label] = _index;
    }

    public void Emit(ILCode op, object? operand = null)
    {
        if (_index >= _insts.Length) {
            Array.Resize(ref _insts, _insts.Length * 2);
        }
        _insts[_index++] = new ILInstruction(op, operand);

        switch (op) {
            case ILCode.Call or ILCode.Callvirt or ILCode.Newobj: {
                var method = (MethodDesc)operand!;
                _stackDepth -= method.ParamSig.Count;
                _stackDepth += method.ReturnType != PrimType.Void ? 1 : 0;
                _stackDepth += (op == ILCode.Newobj) ? 2 : 0; //discount `this` parameter
                break;
            }
            case ILCode.Ldfld or ILCode.Ldflda or ILCode.Stfld: {
                var field = (FieldDesc)operand!;
                _stackDepth -= field.IsInstance ? 1 : 0;
                _stackDepth += (op == ILCode.Stfld) ? -1 : +1;
                break;
            }
            case ILCode.Ret or ILCode.Leave or ILCode.Leave_S or ILCode.Throw or ILCode.Rethrow: {
                _stackDepth = 0;
                break;
            }
            default: {
                Debug.Assert(op.GetStackBehaviourPush() != ILStackBehaviour.Varpush);
                Debug.Assert(op.GetStackBehaviourPop() != ILStackBehaviour.Varpop);
                _stackDepth += op.GetStackChange();
                break;
            }
        }
        _maxStackDepth = Math.Max(_maxStackDepth, _stackDepth);
    }

    public void EmitLoad(Value var) => EmitVarInst(var, 0);
    public void EmitStore(Value var) => EmitVarInst(var, 1);
    public void EmitAddrOf(Value var) => EmitVarInst(var, 2);

    private void EmitVarInst(Value var, int codeTableIdx)
    {
        int index;
        if (var is Argument arg) {
            index = arg.Index;
            codeTableIdx += 3;
        } else if (!_varSlots.TryGetValue((Variable)var, out index)) {
            _varSlots[(Variable)var] = index = _varSlots.Count;
        }
        ref var codes = ref ILTables.VarCodes[codeTableIdx];

        if (index < 4 && codes.Inline != ILCode.Nop) {
            Emit((ILCode)((int)codes.Inline + index));
        } else if (index < 256) {
            Emit(codes.Short, index);
        } else {
            Emit(codes.Normal, index);
        }
    }

    public void EmitLdcI4(int value)
    {
        if (value >= -1 && value <= 8) {
            Emit((ILCode)((int)ILCode.Ldc_I4_0 + value));
        } else if ((sbyte)value == value) {
            Emit(ILCode.Ldc_I4_S, value);
        } else {
            Emit(ILCode.Ldc_I4, value);
        }
    }

    public ILMethodBody Seal(LayoutedCFG layout)
    {
        ComputeOffsets();
        
        return new ILMethodBody() {
            Instructions = new ArraySegment<ILInstruction>(_insts, 0, _index),
            Locals = _varSlots.Keys.ToArray(),
            ExceptionRegions = BuildEHClauses(layout),
            MaxStack = _maxStackDepth,
            InitLocals = true //TODO: preserve InitLocals
        };
    }

    private void ComputeOffsets()
    {
        var insts = GetInstructions();

        //Calculate initial offsets
        int currOffset = 0;
        foreach (ref var inst in insts) {
            inst.Offset = currOffset;
            currOffset += inst.GetSize();
        }

        //Early return for methods with no branches
        if (_labelStarts.Count == 1 && insts[^1].OpCode == ILCode.Ret) return;

        //Optimize branches using a simple greedy algorithm;
        //Better algorithms do exist (https://arxiv.org/pdf/0812.4973.pdf), but they'd probably just
        //save a few hundred bytes at best. Not worth since the result will most likely be recompiled again.
        currOffset = 0;
        foreach (ref var inst in insts) {
            inst.Offset = currOffset;
            currOffset += inst.GetSize();

            if (inst.Operand is ILLabel target) {
                int dist = GetLabelOffset(target) - currOffset;
                var shortCode = ILTables.GetShortBranchCode(inst.OpCode);

                if ((sbyte)dist == dist && shortCode != default) {
                    inst.OpCode = shortCode;
                    currOffset -= 3;
                }
            }
        }

        //Replace label refs with actual offsets
        foreach (ref var inst in insts) {
            if (inst.Operand is ILLabel target) {
                inst.Operand = GetLabelOffset(target);
            } else if (inst.Operand is ILLabel[] targets) {
                inst.Operand = targets.Select(GetLabelOffset).ToArray();
            }
        }
    }

    private ExceptionRegion[] BuildEHClauses(LayoutedCFG layout)
    {
        var clauses = new ExceptionRegion[layout.Regions.Length];

        for (int i = 0; i < clauses.Length; i++) {
            ref var region = ref layout.Regions[i];
            var guard = region.Guard;

            var clause = clauses[i] = new ExceptionRegion() {
                Kind = guard.Kind switch {
                    GuardKind.Catch => guard.HasFilter ? EHRegionKind.Filter : EHRegionKind.Catch,
                    GuardKind.Fault => EHRegionKind.Fault,
                    GuardKind.Finally => EHRegionKind.Finally
                },
                TryStart = GetBlockOffset(region.TryRange.Start),
                TryEnd = GetBlockOffset(region.TryRange.End),
                HandlerStart = GetBlockOffset(region.HandlerRange.Start),
                HandlerEnd = GetBlockOffset(region.HandlerRange.End)
            };
            if (clause.Kind == EHRegionKind.Catch) {
                clause.CatchType = (TypeDefOrSpec?)guard.CatchType;
            }
            if (guard.HasFilter) {
                Debug.Assert(GetBlockOffset(region.FilterRange.End) == clause.HandlerStart);
                clause.FilterStart = GetBlockOffset(region.FilterRange.Start);
            }
        }
        return clauses;

        int GetBlockOffset(int index)
        {
            return index < layout.Blocks.Length
                ? GetLabelOffset(layout.Blocks[index])
                : _insts[_index - 1].GetEndOffset();
        }
    }

    private Span<ILInstruction> GetInstructions()
        => _insts.AsSpan(0, _index);

    private int GetLabelOffset(ILLabel label)
        => _insts[_labelStarts[label]].Offset;

    public override string ToString()
    {
        var sb = new StringBuilder();

        var labels = _labelStarts.ToLookup(e => e.Value, e => e.Key);
        for (int i = 0; i < _index; i++) {
            foreach (var block in labels[i]) {
                sb.Append(block + ":\n");
            }
            sb.Append("  ").Append(_insts[i].ToString().AsSpan("IL_0000: ".Length)).Append("\n");
        }
        return sb.ToString();
    }
}

internal readonly struct ILLabel : IEquatable<ILLabel>
{
    readonly object _token; //Either<BasicBlock, int>

    internal ILLabel(object token) => _token = token;

    public override int GetHashCode() => _token.GetHashCode();
    public override bool Equals(object? obj) => obj is ILLabel other && Equals(other);
    public override string ToString() => "@" + _token;

    public bool Equals(ILLabel other) => _token.Equals(other._token);

    public static implicit operator ILLabel(BasicBlock block) => new(block);
}