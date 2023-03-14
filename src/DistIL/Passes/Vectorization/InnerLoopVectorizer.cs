namespace DistIL.Passes.Vectorization;

using DistIL.Analysis;
using DistIL.IR.Utils;

internal class InnerLoopVectorizer
{
    LoopInfo _loop = null!;
    VectorTranslator _trans = null!;
    Dictionary<Instruction, InstMapping> _mappings = new(); //instructions to be vectorized

    BasicBlock _predBlock = null!;  //block entering the loop
    BasicBlock _bodyBlock = null!;  //single block of the loop
    CompareInst _exitCond = null!;
    PhiInst _counter = null!;       //loop counter (index var)

    IRBuilder _builder = null!;
    Value _newCounter = null!;
    int _width = 4;

    public static bool TryVectorize(LoopInfo loop, VectorTranslator trans, ICompilationLogger? reportLogger)
    {
        //Only consider loops with a single body block (the latch).
        if (loop.NumBlocks != 2) return false;

        var pred = loop.GetPredecessor()!;
        var latch = loop.GetLatch()!;
        var exitCond = loop.GetExitCondition()!;

        if (pred == null || latch == null || exitCond == null) return false;

        if (!(exitCond.Left is PhiInst counter && counter.Block == loop.Header)) return false;

        //Currently we only support loops in the form of:
        //  for (int i = ...; i < bound; i++)
        if (!IsSequentialLoop()) return false;

        //Everything looks right, try to build mappings and vectorize
        var vectorizer = new InnerLoopVectorizer() {
            _loop = loop,
            _trans = trans,
            _predBlock = pred,
            _bodyBlock = latch,
            _exitCond = exitCond,
            _counter = counter
        };
        if (vectorizer.BuildMappings(reportLogger)) {
            vectorizer.EmitVectorizedLoop();
            return true;
        }
        return false;

        bool IsSequentialLoop()
        {
            return
                exitCond.Op is CompareOp.Slt or CompareOp.Ult &&
                counter.GetValue(latch) is BinaryInst { Op: BinaryOp.Add, Right: ConstInt { Value: 1 } } &&
                loop.IsInvariant(exitCond.Right);
        }
    }

    private bool BuildMappings(ICompilationLogger? logger)
    {
        if (_loop.Header.Phis().Count() != 1) return false;

        foreach (var inst in _bodyBlock) {
            switch (inst) {
                case PtrOffsetInst addr: {
                    //TODO: aliasing checks
                    if (!_loop.IsInvariant(addr.BasePtr) || addr.Index != _counter) {
                        return false;
                    }
                    _mappings.Add(inst, default);
                    break;
                }
                case MemoryInst acc: {
                    if (!VectorType.IsSupportedElemType(acc.ElemType)) {
                        logger?.Debug($"AutoVec: unsupported type for memory access '{acc.ElemType}'");
                        return false;
                    }
                    _mappings.Add(inst, new InstMapping() {
                        TargetOp = inst is LoadInst ? VectorOp.Load : VectorOp.Store,
                        ScalarType = acc.ElemType.Kind,
                    });
                    break;
                }
                default: {
                    //Ignore branch or loop counter update
                    if (inst is BranchInst || inst == _counter.GetValue(_bodyBlock)) break;

                    var (op, scalarType) = _trans.GetVectorOp(inst);

                    if (op == VectorOp.Invalid) {
                        logger?.Debug($"AutoVec: unsupported instruction '{inst}'");
                        return false;
                    }
                    _mappings.Add(inst, new InstMapping() {
                        TargetOp = op,
                        ScalarType = scalarType
                    });
                    break;
                }
            }
        }
        //TODO: support any primitive type
        return _mappings.Values.All(m => m.ScalarType == TypeKind.Void || m.ScalarType.Size() == 4);
    }

    public void EmitVectorizedLoop()
    {
        var newLoop = new LoopBuilder(_predBlock, "Vec_");

        _newCounter = newLoop.CreateAccum(
            seed: _counter.GetValue(_predBlock),
            emitUpdate: currIdx => newLoop.Latch.CreateAdd(currIdx, ConstInt.CreateI(_width))
        );

        newLoop.Build(
            emitCond: builder => {
                var newBound = newLoop.PreHeader.CreateSub(_exitCond.Right, ConstInt.CreateI(_width - 1));
                return builder.CreateSlt(_newCounter, newBound);
            },
            emitBody: builder => {
                _builder = builder;

                foreach (var inst in _mappings.Keys) {
                    ref var mapping = ref _mappings.GetRef(inst);
                    mapping.ClonedDef = VectorizeInst(inst, ref mapping);
                }
            }
        );

        newLoop.Exit.SetBranch(_loop.Header);

        _predBlock.SetBranch(newLoop.EntryBlock);
        _counter.RedirectArg(_predBlock, newLoop.Exit.Block, _newCounter);
    }

    private Instruction VectorizeInst(Instruction inst, ref InstMapping mapping)
    {
        if (inst is PtrOffsetInst addr) {
            Debug.Assert(!addr.KnownStride || addr.Stride == addr.ElemType.Kind.Size());

            return (Instruction)_builder.CreatePtrOffset(
                GetScalarMapping(addr.BasePtr),
                GetScalarMapping(addr.Index),
                addr.ElemType
            );
        }

        Debug.Assert(mapping.TargetOp != VectorOp.Invalid);

        var args = new Value[inst.Operands.Length];
        for (int i = 0; i < args.Length; i++) {
            args[i] = GetVectorMapping(inst.Operands[i]);
        }
        //Store signature is (this, destPtr) rather than (destPtr, al)
        if (mapping.TargetOp == VectorOp.Store) {
            (args[0], args[1]) = (args[1], args[0]);
        }

        var vecType = new VectorType(mapping.ScalarType, _width);
        return _trans.EmitOp(_builder, vecType, mapping.TargetOp, args);
    }
    private Value GetVectorMapping(Value scalar)
    {
        if (scalar is Instruction inst && inst.Block == _bodyBlock) {
            return _mappings.GetRef(inst).ClonedDef
                ?? throw new InvalidOperationException();
        }
        var vecType = new VectorType(scalar.ResultType.Kind, _width);

        //TODO: cache and hoist invariant/constant vectors
        if (scalar == _counter) {
            var seqOffsets = Enumerable.Range(0, _width)
                    .Select(i => ConstInt.Create(scalar.ResultType, i))
                    .ToArray();

            //v_idx = [idx, idx, ...] + [0, 1, ...]
            return _trans.EmitOp(_builder, vecType, VectorOp.Add,
                _trans.EmitOp(_builder, vecType, VectorOp.Splat, _newCounter),
                _trans.EmitOp(_builder, vecType, VectorOp.Pack, seqOffsets)
            );
        }
        if (_loop.IsInvariant(scalar)) {
            return _trans.EmitOp(_builder, vecType, VectorOp.Splat, scalar);
        }
        throw new UnreachableException();
    }
    private Value GetScalarMapping(Value val)
    {
        if (val == _counter) {
            return _newCounter;
        }
        Debug.Assert(_loop.IsInvariant(val));
        return val;
    }
}

internal struct InstMapping
{
    public Instruction? ClonedDef;
    public VectorOp TargetOp;
    public TypeKind ScalarType;

    //Whether this is a load/store of an small int type (byte/short), implicitly widened to a int.
    public bool IsWidenedMemAcc;
}