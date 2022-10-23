namespace DistIL.CodeGen.Cil;

using DistIL.Analysis;

public partial class ILGenerator : InstVisitor
{
    MethodBody _method;
    ForestAnalysis _forest;
    ILAssembler _asm = new();
    Dictionary<Instruction, Variable> _instSlots = new();

    BasicBlock? _nextBlock;

    public ILGenerator(MethodBody method)
    {
        _method = method;
        _forest = new ForestAnalysis(method);
    }

    public ILMethodBody Generate()
    {
        var layout = LayoutedCFG.Compute(_method);
        var blocks = layout.Blocks;

        for (int i = 0; i < blocks.Length; i++) {
            var block = blocks[i];
            _nextBlock = i + 1 < blocks.Length ? blocks[i + 1] : null;

            //Insert nop to serve as an anchor if there's a loop outside this region, e.g:
            //  while (true) { try { ... } }    ->  
            //  BB_01: try; ...; leave BB_01;   ->
            //  IL_00: nop; IL_01: ...; IL_xx: leave IL_00;
            //Blocks with a single jump are also considered, so this will occasionaly
            //false trigger, but it should cover all cases and only cost a single byte.
            //FIXME: This may not work properly if there's no predecessor block,
            //       we should check for this in the layoutizer or when assembling regions
            //FIXME: Check if we actually need this (it looks perfectly fine, but ILSpy decomps it weirdly)
            if (block.First is GuardInst && block.Preds.Any(p => p.Last is LeaveInst || p.First is BranchInst { IsJump: true })) {
                _asm.Emit(ILCode.Nop);
            }
            //If this is the entry block of a handler/filter, pop the exception to the guard variable
            var guard = block.Users().FirstOrDefault(u => u is GuardInst { Kind: not GuardKind.Finally });

            _asm.StartBlock(block, guard != null);

            if (guard != null) {
                _asm.EmitStore(GetSlot(guard));
            }
            EmitBlock(block);
        }
        return _asm.Seal(layout);
    }

    private void EmitBlock(BasicBlock block)
    {
        //Emit code for the rooted trees in the forest
        foreach (var inst in block) {
            if (!_forest.IsTreeRoot(inst) || inst is GuardInst) continue;

            inst.Accept(this);

            if (inst.NumUses > 0) {
                _asm.EmitStore(GetSlot(inst));
            } else if (inst.HasResult) {
                _asm.Emit(ILCode.Pop); //unused result
            }
        }
    }

    private void Push(Value value)
    {
        switch (value) {
            case Variable or Argument: {
                _asm.EmitLoad(value);
                break;
            }
            case ConstInt cons: {
                //Emit int, or small long followed by conv.i8
                if (cons.IsInt || (cons.Value == (int)cons.Value)) {
                    _asm.EmitLdcI4((int)cons.Value);
                    if (!cons.IsInt) _asm.Emit(ILCode.Conv_I8);
                } else {
                    _asm.Emit(ILCode.Ldc_I8, cons.Value);
                }
                break;
            }
            case ConstFloat cons: {
                if (cons.IsSingle) {
                    _asm.Emit(ILCode.Ldc_R4, (float)cons.Value);
                } else {
                    _asm.Emit(ILCode.Ldc_R8, cons.Value);
                }
                break;
            }
            case ConstString cons: {
                _asm.Emit(ILCode.Ldstr, cons.Value);
                break;
            }
            case ConstNull: {
                _asm.Emit(ILCode.Ldnull);
                break;
            }
            case Instruction inst: {
                if (_forest.IsLeaf(inst)) {
                    inst.Accept(this);
                } else {
                    _asm.EmitLoad(GetSlot(inst));
                }
                break;
            }
            default: throw new NotSupportedException(value.GetType().Name + " as operand");
        }
    }
    
    private void EmitFallthrough(BasicBlock target)
    {
        if (_nextBlock != target) {
            _asm.Emit(ILCode.Br, target);
        }
    }

    private Variable GetSlot(Instruction inst)
    {
        return _instSlots.GetOrAddRef(inst) ??= new(inst.ResultType, name: $"expr{_instSlots.Count}");
    }
}