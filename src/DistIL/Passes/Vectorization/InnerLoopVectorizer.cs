namespace DistIL.Passes.Vectorization;

using DistIL.Analysis;
using DistIL.IR.Utils;

internal class InnerLoopVectorizer
{
    LoopInfo _loop = null!;
    VectorTranslator _trans = null!;
    Dictionary<Instruction, InstMapping> _mappings = new(); //instructions to be vectorized
    Dictionary<PhiInst, (Value Seed, Instruction IterOut, PhiInst NewPhi, VectorOp Op)> _reductionPhis = new();

    BasicBlock _predBlock = null!;  //block entering the loop
    BasicBlock _bodyBlock = null!;  //loop body and latch
    CompareInst _exitCond = null!;
    PhiInst _counter = null!;       //loop counter (index var)

    Value _newCounter = null!;
    int _width = 8;

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
        foreach (var phi in _loop.Header.Phis()) {
            if (phi == _counter) continue;

            if (phi.GetValue(_bodyBlock) is not Instruction iterOut || iterOut.Block != _bodyBlock) return false;

            var op = _trans.GetVectorOp(iterOut).Op;
            if (!IsSupportedReductionOp(op)) return false;

            _reductionPhis.Add(phi, new() {
                Seed = phi.GetValue(_predBlock),
                IterOut = iterOut,
                Op = op
            });
        }

        foreach (var inst in _bodyBlock) {
            switch (inst) {
                case PtrOffsetInst addr: {
                    //TODO: aliasing checks
                    if (!_loop.IsInvariant(addr.BasePtr) || addr.Index != _counter) {
                        return false;
                    }
                    _mappings.Add(inst, new());
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

    private static bool IsSupportedReductionOp(VectorOp op)
    {
        return op is
            VectorOp.Add or VectorOp.Mul or
            VectorOp.And or VectorOp.Or or VectorOp.Xor or
            VectorOp.Min or VectorOp.Max;
    }

    public void EmitVectorizedLoop()
    {
        var newLoop = new LoopBuilder(_predBlock, "Vec_");

        _newCounter = newLoop.CreateAccum(
            seed: _counter.GetValue(_predBlock),
            emitUpdate: currIdx => newLoop.Latch.CreateAdd(currIdx, ConstInt.CreateI(_width))
        ).SetName("v_idx");

        //Create reduction accumulator phis
        foreach (var phi in _reductionPhis.Keys) {
            ref var data = ref _reductionPhis.GetRef(phi);

            var vecType = new VectorType(phi.ResultType, _width);
            var seed = data.Op switch {
                //Additive: [seed, 0, 0, ...]
                VectorOp.Add or VectorOp.Or or VectorOp.Xor
                    => EmitAccumOpSeed(data.Seed, 0),
                //Multiplicative: [seed, 1, 1, ...]
                VectorOp.Mul
                    => EmitAccumOpSeed(data.Seed, 1),
                //Exclusive: [seed, seed, ...]
                VectorOp.And or VectorOp.Min or VectorOp.Max
                    => _trans.EmitOp(newLoop.PreHeader, vecType, VectorOp.Splat, data.Seed),
            };
            data.NewPhi = newLoop.CreateAccum(seed, currVal => currVal);

            Value EmitAccumOpSeed(Value seed, int identity)
            {
                Value rest = seed.ResultType.IsFloat()
                    ? ConstFloat.Create(seed.ResultType, identity) 
                    : ConstInt.Create(seed.ResultType, identity);

                var args = new Value[_width];
                args[0] = seed;
                args.AsSpan(1..).Fill(rest);

                return _trans.EmitOp(newLoop.PreHeader, vecType, VectorOp.Pack, args);
            }
        }

        //Emit loop and widen instructions
        newLoop.Build(
            emitCond: builder => {
                var newBound = newLoop.PreHeader.CreateSub(_exitCond.Right, ConstInt.CreateI(_width - 1)).SetName("v_bound");
                return builder.CreateSlt(_newCounter, newBound);
            },
            emitBody: builder => {
                foreach (var inst in _mappings.Keys) {
                    ref var mapping = ref _mappings.GetRef(inst);
                    mapping.ClonedDef = VectorizeInst(newLoop, inst, ref mapping);
                }
            }
        );

        //Finalize reductions
        foreach (var (phi, data) in _reductionPhis) {
            var vecType = new VectorType(phi.ResultType, _width);
            var finalValue = _trans.EmitReduction(newLoop.Exit, vecType, data.Op, data.NewPhi);

            data.NewPhi.SetValue(newLoop.Latch.Block, _mappings.GetRef(data.IterOut).ClonedDef!);
            phi.RedirectArg(_predBlock, newLoop.Exit.Block, finalValue);
        }

        newLoop.Exit.SetBranch(_loop.Header);

        _predBlock.SetBranch(newLoop.EntryBlock);
        _counter.RedirectArg(_predBlock, newLoop.Exit.Block, _newCounter);
    }

    private Instruction VectorizeInst(LoopBuilder newLoop, Instruction inst, ref InstMapping mapping)
    {
        if (inst is PtrOffsetInst addr) {
            Debug.Assert(!addr.KnownStride || addr.Stride == addr.ElemType.Kind.Size());

            return (Instruction)newLoop.Body.CreatePtrOffset(
                GetScalarMapping(addr.BasePtr),
                GetScalarMapping(addr.Index),
                addr.ElemType
            );
        }

        Debug.Assert(mapping.TargetOp != VectorOp.Invalid);

        var args = new Value[inst.Operands.Length];
        for (int i = 0; i < args.Length; i++) {
            var arg = inst.Operands[i];
            args[i] = GetVectorMapping(newLoop.Body, arg);
        }
        var vecType = new VectorType(mapping.ScalarType, _width);
        return _trans.EmitOp(newLoop.Body, vecType, mapping.TargetOp, args);
    }
    private Value GetVectorMapping(IRBuilder builder, Value scalar)
    {
        if (scalar is Instruction inst && inst.Block == _bodyBlock) {
            return _mappings.GetRef(inst).ClonedDef
                ?? throw new InvalidOperationException();
        }
        if (scalar is PhiInst phi && _reductionPhis.ContainsKey(phi)) {
            return _reductionPhis.GetRef(phi).NewPhi;
        }
        var vecType = new VectorType(scalar.ResultType.Kind, _width);

        //TODO: cache and hoist invariant/constant vectors
        if (scalar == _counter) {
            var seqOffsets = Enumerable.Range(0, _width)
                    .Select(i => ConstInt.Create(scalar.ResultType, i))
                    .ToArray();

            //v_idx = [idx, idx, ...] + [0, 1, ...]
            return _trans.EmitOp(builder, vecType, VectorOp.Add,
                _trans.EmitOp(builder, vecType, VectorOp.Splat, _newCounter),
                _trans.EmitOp(builder, vecType, VectorOp.Pack, seqOffsets)
            );
        }
        if (_loop.IsInvariant(scalar)) {
            return _trans.EmitOp(builder, vecType, VectorOp.Splat, scalar);
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