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

        var pred = loop.GetPredecessor();
        var latch = loop.GetLatch();
        var exitCond = loop.GetExitCondition();

        if (pred == null || latch == null || exitCond == null) return false;

        if (!(exitCond.Left is PhiInst counter && counter.Block == loop.Header)) return false;

        //Currently we only support loops in the form of:
        //  for (int i = ...; i < bound; i++)
        //  for (ptr p = ...; p < bound; p = lea p + 1)
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
        if (vectorizer.BuildMappings(reportLogger) && vectorizer.PickVectorWidth()) {
            vectorizer.EmitVectorizedLoop();
            return true;
        }
        return false;

        bool IsSequentialLoop()
        {
            return exitCond.Op is CompareOp.Slt or CompareOp.Ult && 
                   loop.IsInvariant(exitCond.Right) &&
                   counter.GetValue(latch) is var iterOut &&
                   (iterOut is BinaryInst { Op: BinaryOp.Add, Right: ConstInt { Value: 1 } } ||
                   (iterOut is PtrOffsetInst { BasePtr: var basePtr, Index: ConstInt { Value: 1 }, KnownStride: true } && basePtr == counter));
        }
    }

    private bool BuildMappings(ICompilationLogger? logger)
    {
        foreach (var phi in _loop.Header.Phis()) {
            if (phi == _counter) continue;

            if (phi.GetValue(_bodyBlock) is not Instruction iterOut || iterOut.Block != _bodyBlock) return false;
            if (phi.Users().Any(u => _loop.Contains(u.Block) && u != iterOut)) return false;

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
                    if ((!_loop.IsInvariant(addr.BasePtr) || addr.Index != _counter) && addr.BasePtr is not PhiInst) {
                        logger?.Debug($"AutoVec: unsupported pointer addressing '{addr}'");
                        return false;
                    }
                    _mappings.Add(inst, new());
                    break;
                }
                case MemoryInst acc: {
                    if (!VectorType.IsSupportedElemType(acc.ElemType)) {
                        logger?.Debug($"AutoVec: unsupported memory access type '{acc.ElemType}'");
                        return false;
                    }
                    _mappings.Add(inst, new InstMapping() {
                        TargetOp = inst is LoadInst ? VectorOp.Load : VectorOp.Store,
                        ElemType = acc.ElemType.Kind,
                    });
                    break;
                }
                default: {
                    //Ignore branch or loop counter update
                    if (inst is BranchInst || inst == _counter.GetValue(_bodyBlock)) break;

                    var (op, elemType) = _trans.GetVectorOp(inst);

                    if (op == VectorOp.Invalid) {
                        logger?.Debug($"AutoVec: unsupported instruction '{inst}'");
                        return false;
                    }
                    if (inst.ResultType == PrimType.Bool) {
                        elemType = GetConditionalElemType(inst);
                        if (elemType == TypeKind.Void) {
                            logger?.Debug("AutoVec: mismatching conditional element width (compares of different types?)");
                            return false;
                        }
                        if (!inst.Users().All(u => IsSupportedConditionalUse(u, elemType))) {
                            logger?.Debug("AutoVec: unsupported use of bool-returning instruction");
                            return false;
                        }
                    }
                    if (IsPredicatedCountStep(inst)) {
                        var rhs = (Instruction)inst.Operands[1];
                        elemType = _mappings.GetRef(rhs).ElemType;
                        op = VectorOp._PredicatedCount;
                    }
                    _mappings.Add(inst, new InstMapping() {
                        TargetOp = op,
                        ElemType = elemType
                    });
                    break;
                }
            }
        }
        return true;
    }

    private bool PickVectorWidth()
    {
        //Check if all mappings have the same scalar width
        //TODO: support for partial unrolling
        int commonElemSize = 0;

        foreach (var inst in _mappings.Keys) {
            ref var mapping = ref _mappings.GetRef(inst);

            if (mapping.ElemType == TypeKind.Void) continue;

            int size = mapping.ElemType.BitSize();

            if (commonElemSize == 0) {
                commonElemSize = size;
            } else if (commonElemSize != size) {
                return false;
            }
        }
        //TODO: figure out when it's better to use 128-bit vectors (ARM/old CPUs)
        _width = 256 / commonElemSize;
        return true;
    }

    private void EmitVectorizedLoop()
    {
        var newLoop = new LoopBuilder(_predBlock, "Vec_");
        var steppedCounter = _counter.GetValue(_bodyBlock);
        int stepSize = steppedCounter is PtrOffsetInst lea ? _width * lea.Stride : _width;

        _newCounter = newLoop.CreateAccum(
            seed: _counter.GetValue(_predBlock),
            emitUpdate: currVal => {
                var step = ConstInt.CreateI(_width);

                return steppedCounter is PtrOffsetInst lea
                    ? newLoop.Latch.CreatePtrOffset(currVal, step, lea.ElemType)
                    : newLoop.Latch.CreateAdd(currVal, step);
            }
        ).SetName("v_idx");

        //Create reduction accumulator phis
        foreach (var phi in _reductionPhis.Keys) {
            CreateReductionPhi(newLoop, phi);
        }

        //Emit loop and widen instructions
        newLoop.Build(
            emitCond: builder => {
                if (steppedCounter is PtrOffsetInst) {
                    var remBytes = builder.CreateSub(_exitCond.Right, _newCounter);
                    return builder.CreateSge(remBytes, ConstInt.CreateI(stepSize));
                }
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

        //Finalize reduction phis
        foreach (var (phi, data) in _reductionPhis) {
            Value exitValue;

            if (_trans.IsVectorType(data.NewPhi.ResultType)) {
                var vecType = new VectorType(phi.ResultType, _width);
                exitValue = _trans.EmitReduction(newLoop.Exit, vecType, data.Op, data.NewPhi);
            } else {
                exitValue = data.NewPhi;
            }
            data.NewPhi.SetValue(newLoop.Latch.Block, _mappings.GetRef(data.IterOut).ClonedDef!);
            phi.RedirectArg(_predBlock, newLoop.Exit.Block, exitValue);
        }

        newLoop.Exit.SetBranch(_loop.Header);

        _predBlock.SetBranch(newLoop.EntryBlock);
        _counter.RedirectArg(_predBlock, newLoop.Exit.Block, _newCounter);

        // If the number of iterations is a constant multiple of vector width,
        // kill the old loop by setting its exit cond to false and leave it for DCE.
        if (_exitCond.Right is ConstInt constBound && constBound.Value % stepSize == 0) {
            _exitCond.ReplaceWith(ConstInt.CreateI(0));
        }
    }

    private Value VectorizeInst(LoopBuilder newLoop, Instruction inst, ref InstMapping mapping)
    {
        if (inst is PtrOffsetInst addr) {
            Debug.Assert(!addr.KnownStride || addr.Stride == addr.ElemType.Kind.Size());

            return newLoop.Body.CreatePtrOffset(
                GetScalarMapping(addr.BasePtr),
                GetScalarMapping(addr.Index),
                addr.ElemType
            );
        }
        Debug.Assert(mapping.TargetOp != VectorOp.Invalid);

        var vecType = new VectorType(mapping.ElemType, _width);
        var args = new Value[inst.Operands.Length];

        for (int i = 0; i < args.Length; i++) {
            var arg = inst.Operands[i];
            args[i] = GetVectorMapping(newLoop.Body, vecType, arg);
        }
        return _trans.EmitOp(newLoop.Body, vecType, mapping.TargetOp, args);
    }

    private Value GetVectorMapping(IRBuilder builder, VectorType vecType, Value scalar)
    {
        if (scalar is Instruction inst && inst.Block == _bodyBlock) {
            return _mappings.GetRef(inst).ClonedDef
                ?? throw new InvalidOperationException();
        }
        if (scalar is PhiInst phi && _reductionPhis.ContainsKey(phi)) {
            return _reductionPhis.GetRef(phi).NewPhi;
        }

        //TODO: cache and hoist invariant/constant vectors
        if (scalar == _counter && scalar.ResultType.IsInt()) {
            var seqOffsets = Enumerable.Range(0, vecType.Count)
                    .Select(i => ConstInt.Create(vecType.ElemType, i))
                    .ToArray();

            //v_idx = [idx, idx, ...] + [0, 1, ...]
            return _trans.EmitOp(builder, vecType, VectorOp.Add,
                _trans.EmitOp(builder, vecType, VectorOp.Splat, _newCounter),
                _trans.EmitOp(builder, vecType, VectorOp.Pack, seqOffsets)
            );
        }
        if (scalar == _counter) {
            Debug.Assert(_counter.ResultType.IsPointerLike());
            return _newCounter;
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

    private void CreateReductionPhi(LoopBuilder newLoop, PhiInst phi)
    {
        ref var data = ref _reductionPhis.GetRef(phi);
        var vecType = new VectorType(phi.ResultType, _width);

        var seed = data.Op switch {
            //For bool sums, it's easier to use scalar phi + movemask
            VectorOp.Add when IsPredicatedCountStep(data.IterOut)
                => data.Seed,
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
            var identityConst = seed.ResultType.IsFloat()
                ? ConstFloat.Create(seed.ResultType, identity)
                : ConstInt.Create(seed.ResultType, identity) as Const;

            var args = new Value[_width];
            args[0] = seed;
            args.AsSpan(1..).Fill(identityConst);

            return _trans.EmitOp(newLoop.PreHeader, vecType, VectorOp.Pack, args);
        }
    }
    //Checks if we know how to vectorize a reduction with the given accum op
    private static bool IsSupportedReductionOp(VectorOp op)
    {
        return op is
            VectorOp.Add or VectorOp.Mul or
            VectorOp.And or VectorOp.Or or VectorOp.Xor or
            VectorOp.Min or VectorOp.Max;
    }

    //Returns the nominal element type for some conditional (e.g. `cmp i32, i32` -> i32)
    private TypeKind GetConditionalElemType(Instruction inst)
    {
        //Widen conditionals like ((cmp x, y) | (cmp z, w)) to their comparand scalar widths
        if (inst is BinaryInst or CompareInst) {
            var type = TypeKind.Void;
            if (inst.Operands[0] is Instruction lhs && _mappings.ContainsKey(lhs)) {
                type = _mappings.GetRef(lhs).ElemType;
            }
            if (inst.Operands[1] is Instruction rhs && _mappings.ContainsKey(rhs)) {
                var otherType = _mappings.GetRef(rhs).ElemType;
                if (type.Size() != otherType.Size()) {
                    return TypeKind.Void;
                }
            }
            return type;
        }
        return TypeKind.Void;
    }
    //Checks if we know how to vectorize a conditional (inst returning bool) with the given user
    private static bool IsSupportedConditionalUse(Instruction user, TypeKind nominalElemType)
    {
        return
            (user is SelectInst sel && sel.ResultType.Kind.Size() == nominalElemType.Size()) ||
            (user is BinaryInst { Op: BinaryOp.And or BinaryOp.Or or BinaryOp.Xor, ResultType.Kind: TypeKind.Bool }) ||
            IsPredicatedCountStep(user);
    }
    //Checks if `inst` is an `add (int32)phi, (bool)cond`.
    private static bool IsPredicatedCountStep(Instruction inst)
    {
        return inst is BinaryInst {
            Op: BinaryOp.Add,
            Left: PhiInst phi,
            Right.ResultType.Kind: TypeKind.Bool,
            NumUses: 1
        } bin && inst.Users().First() == phi;
    }
}

internal struct InstMapping
{
    public Value? ClonedDef;
    public VectorOp TargetOp;
    public TypeKind ElemType;

    //Whether this is a load/store of an small int type (byte/short), implicitly widened to a int.
    public bool IsWidenedMemAcc;
}