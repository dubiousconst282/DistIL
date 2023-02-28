namespace DistIL.CodeGen.Cil;

using DistIL.Analysis;

public partial class ILGenerator : InstVisitor
{
    readonly MethodBody _method;
    readonly RegisterAllocator _regAlloc;
    readonly ForestAnalysis _forest;
    readonly ILAssembler _asm = new();

    BasicBlock? _currBlock, _nextBlock;
    ParallelCopyEmitter? _pcopyEmitter;

    private ILGenerator(MethodBody method)
    {
        _method = method;
        _regAlloc = new RegisterAllocator(method); //may split critical edges, must be created first
        _forest = new ForestAnalysis(method);
    }

    public static ILMethodBody GenerateCode(MethodBody method)
    {
        return new ILGenerator(method).Generate();
    }

    private ILMethodBody Generate()
    {
        var layout = LayoutedCFG.Compute(_method);
        var blocks = layout.Blocks;

        for (int i = 0; i < blocks.Length; i++) {
            _currBlock = blocks[i];
            _nextBlock = i + 1 < blocks.Length ? blocks[i + 1] : null;

            //Note that ILSpy generates code with two loops for regions inside a loop. (BB_Head: guard; leave BB_Head;)
            //Roslyn emits a nop before the head for such cases, but it does not seem to affect behavior.
            //CoreCLR throws InvalidProgram if there's any instruction after leave/endfinally (incl. nop).
            var guard = _currBlock.Users().FirstOrDefault(u => u is GuardInst { Kind: GuardKind.Catch });
            _asm.StartBlock(_currBlock, guard != null);

            //If this is the entry block of a handler/filter, pop the exception to the guard variable
            if (guard != null) {
                StoreResult(guard);
            }

            //Emit code for all statements
            foreach (var inst in _currBlock) {
                if (!_forest.IsTreeRoot(inst) || inst is GuardInst or PhiInst) continue;

                inst.Accept(this);
                StoreResult(inst);
            }
        }
        return _asm.Seal(layout);
    }

    private void StoreResult(Instruction def)
    {
        if (def.NumUses > 0) {
            var reg = _regAlloc.GetRegister(def);
            _asm.EmitStore(reg);
        } else if (def.HasResult) {
            _asm.Emit(ILCode.Pop); //unused
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
                    var reg = _regAlloc.GetRegister(inst);
                    _asm.EmitLoad(reg);
                }
                break;
            }
            default: throw new NotSupportedException(value.GetType().Name + " as operand");
        }
    }
    
    private void EmitFallthrough(ILCode code, BasicBlock target)
    {
        EmitOutgoingPhiCopies();

        if (_nextBlock != target || code != ILCode.Br) {
            _asm.Emit(code, (ILLabel)target);
        }
    }

    private void EmitBranchAndFallthrough(ILCode code, object operand, BasicBlock fallthrough)
    {
        EmitOutgoingPhiCopies();

        Debug.Assert(operand is ILLabel or ILLabel[]);
        _asm.Emit(code, operand);

        if (_nextBlock != fallthrough) {
            _asm.Emit(ILCode.Br, (ILLabel)fallthrough);
        }
    }

    //Emits phi-related copies for outgoing values in this block.
    //This should be called just before a branch is emitted.
    private void EmitOutgoingPhiCopies()
    {
        var copies = _regAlloc.GetPhiCopies(_currBlock!);
        if (copies == null) return;

        foreach (var (phi, value) in copies) {
            var destReg = _regAlloc.GetRegister(phi);

            if (value is Instruction valueI) {
                var srcReg = _regAlloc.GetRegister(valueI);
                _pcopyEmitter ??= new();
                _pcopyEmitter.Add(destReg, srcReg);
            } else {
                Push(value);
                _asm.EmitStore(destReg);
            }
        }

        if (_pcopyEmitter?.Count > 0) {
            _pcopyEmitter.SequentializeAndClear((dest, src) => {
                _asm.EmitLoad(src);
                _asm.EmitStore(dest);
            });
        }
    }
}