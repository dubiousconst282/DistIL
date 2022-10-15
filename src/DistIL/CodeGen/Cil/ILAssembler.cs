namespace DistIL.CodeGen.Cil;

using EHRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

/// <summary> Helper for building a list of <see cref="ILInstruction"/>s. </summary>
internal class ILAssembler
{
    List<ILInstruction> _insts = new();
    Dictionary<Variable, int> _varSlots = new();
    Dictionary<BasicBlock, int> _blockStarts = new();
    int _stackDepth = 0, _maxStackDepth = 0;

    /// <summary> Specifies that next emitted instructions belongs to the specified block. </summary>
    public void StartBlock(BasicBlock block)
    {
        _blockStarts.Add(block, _insts.Count);
    }

    public void Emit(ILCode op, object? operand = null)
    {
        _insts.Add(new ILInstruction(op, operand));

        switch (op) {
            case ILCode.Call or ILCode.Callvirt or ILCode.Newobj: {
                var method = (MethodDesc)operand!;
                _stackDepth += method.Params.Length - (op == ILCode.Newobj ? 1 : 0);
                _stackDepth -= method.HasResult ? 1 : 0;
                break;
            }
            case ILCode.Ldfld or ILCode.Ldflda or ILCode.Stfld: {
                var field = (FieldDesc)operand!;
                _stackDepth -= field.IsInstance ? 1 : 0;
                _stackDepth -= op == ILCode.Stfld ? 1 : 0;
                break;
            }
            case ILCode.Ret: break; //depth is reset after block terminators
            default: {
                Debug.Assert(op.GetStackBehaviourPush() != ILStackBehaviour.Varpush);
                Debug.Assert(op.GetStackBehaviourPop() != ILStackBehaviour.Varpop);
                _stackDepth += op.GetStackChange();
                break;
            }
        }
        _maxStackDepth = Math.Max(_maxStackDepth, _stackDepth);

        if (op.IsTerminator()) {
            _stackDepth = 0;
        }
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
            ExceptionRegions = ComputeEHRegions(layout),
            MaxStack = _maxStackDepth,
            Instructions = _insts,
            Locals = _varSlots.Keys.ToList(),
            InitLocals = true //TODO: preserve InitLocals
        };
    }

    private void ComputeOffsets()
    {
        var insts = _insts.AsSpan();

        //Calculate initial offsets
        int currOffset = 0;
        foreach (ref var inst in insts) {
            inst.Offset = currOffset;
            currOffset += inst.GetSize();
        }

        //Early return for methods with no branches
        if (_blockStarts.Count == 1 && insts[^1].OpCode == ILCode.Ret) return;

        //Optimize branches using a simple greedy algorithm;
        //Better algorithms do exist (https://arxiv.org/pdf/0812.4973.pdf), but they'd probably just
        //save a few hundred bytes at best. Not worth since the result will most likely be recompiled again.
        currOffset = 0;
        foreach (ref var inst in insts) {
            inst.Offset = currOffset;
            currOffset += inst.GetSize();

            if (inst.Operand is BasicBlock target) {
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
            if (inst.Operand is BasicBlock target) {
                inst.Operand = GetLabelOffset(target);
            } else if (inst.Operand is BasicBlock[] targets) {
                inst.Operand = targets.Select(GetLabelOffset).ToArray();
            }
        }
    }

    private int GetLabelOffset(BasicBlock block)
    {
        return _insts[_blockStarts[block]].Offset;
    }

    private List<ExceptionRegion> ComputeEHRegions(LayoutedCFG layout)
    {
        var ehRegions = new List<ExceptionRegion>(layout.Regions.Length);
        foreach (ref var region in layout.Regions.AsSpan()) {
            var guard = region.Guard;
            
            var ehr = new ExceptionRegion() {
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
            if (ehr.Kind == EHRegionKind.Catch) {
                ehr.CatchType = (TypeDefOrSpec?)guard.CatchType;
            }
            if (guard.HasFilter) {
                Debug.Assert(GetBlockOffset(region.FilterRange.End) == ehr.HandlerStart);
                ehr.FilterStart = GetBlockOffset(region.FilterRange.Start);
            }
            ehRegions.Add(ehr);
        }
        int GetBlockOffset(int index)
        {
            return index < layout.Blocks.Length
                ? GetLabelOffset(layout.Blocks[index])
                : _insts[^1].GetEndOffset();
        }

        return ehRegions;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (ref var inst in _insts.AsSpan()) {
            sb.AppendLine(inst.ToString());
        }
        return sb.ToString();
    }
}