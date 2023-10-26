namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

/// <summary> Source based on a continuous memory location: Array, List&lt;T>, or string. </summary>
internal class MemorySource : LinqSourceNode
{
    public MemorySource(UseRef source, LinqStageNode drain)
        : base(drain, source) { }

    Value? _currPtr, _endPtr;

    protected override void EmitHead(LoopBuilder loop, out Value? length, ref LinqStageNode firstStage)
    {
        var builder = loop.PreHeader;
        //T& startPtr = call MemoryMarshal.GetArrayDataReference<T>(T[]: source)  //or akin.
        (_currPtr, length) = LoopStrengthReduction.CreateGetDataPtrRange(builder, PhysicalSource.Operand);

        IntegrateSkipTakeRanges(builder, ref firstStage, out var offset, ref length);
        if (offset != null) {
            _currPtr = builder.CreatePtrOffset(_currPtr, offset);
        }

        //T& endPtr = lea startPtr + (nint)length * sizeof(T)
        _endPtr = builder.CreatePtrOffset(_currPtr, length);

        //T& currPtr = phi [PreHeader: startPtr], [Latch: {currPtr + sizeof(T)}]
        _currPtr = loop.CreateAccum(_currPtr, currPtr => loop.Latch.CreatePtrIncrement(currPtr)).SetName("lq_currPtr");
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateUlt(_currPtr!, _endPtr!); //ptr < endPtr

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreateLoad(_currPtr!); //*ptr
}
internal class EnumeratorSource : LinqSourceNode
{
    public EnumeratorSource(UseRef enumerable, LinqStageNode drain)
        : base(drain, enumerable) { }

    Value? _enumerator;

    protected override void EmitHead(LoopBuilder loop, out Value? length, ref LinqStageNode firstStage)
    {
        var builder = loop.PreHeader;

        var sourceCall = (CallInst)PhysicalSource.Parent;
        var enumerableType = sourceCall.Method.ParamSig[PhysicalSource.OperIndex].Type; // IEnumerable<T> | IEnumerable

        // Find struct enumerator type
        // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/iteration-statements#the-foreach-statement
        if (PhysicalSource.Operand is CilIntrinsic.Box box && enumerableType.IsGeneric) {
            var getEnumer = box.SourceType.Methods.FirstOrDefault(m => m is { Name: "GetEnumerator", IsInstance: true, ParamSig.Count: 1 });

            if (getEnumer != null && IsValidCustomEnumeratorType(getEnumer.ReturnType, enumerableType.GenericParams[0])) {
                _enumerator = builder.CreateCallVirt(getEnumer, builder.CreateUnboxRef(box.SourceType, box));

                if (_enumerator.ResultType.IsValueType) {
                    var slot = new LocalSlot(_enumerator.ResultType, "lq_srcIter");
                    builder.CreateStore(slot, _enumerator);
                    _enumerator = slot;
                }
            }
        }

        if (_enumerator == null) {
            _enumerator = builder.CreateCallVirt(enumerableType.FindMethod("GetEnumerator"), PhysicalSource.Operand);
        }
        length = null;

        Debug.Assert(!_enumerator.ResultType.IsValueType);
    }

    private static bool IsValidCustomEnumeratorType(TypeDesc type, TypeDesc elemType)
    {
        bool foundMoveNext = false, foundGetCurrent = false, foundDuplicate = false;

        foreach (var method in type.Methods) {
            bool isAccessible = method is { IsPublic: true, IsInstance: true, ParamSig.Count: 1 };

            if (method.Name == "MoveNext") {
                foundDuplicate |= foundMoveNext;
                foundMoveNext = isAccessible && method.ReturnType == PrimType.Bool;
            }
            if (method.Name == "get_Current") {
                foundDuplicate |= foundGetCurrent;
                foundGetCurrent = isAccessible && method.ReturnType == elemType;
            }
        }
        return foundMoveNext && foundGetCurrent && !foundDuplicate;
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateCallVirt("MoveNext", _enumerator!);

    protected override Value EmitCurrent(IRBuilder builder)
        => builder.CreateCallVirt("get_Current", _enumerator!);

    protected override void EmitEnd(LoopBuilder loop)
    {
        var t_IDisposable = loop.Body.Resolver.Import(typeof(IDisposable));

        // Bail on non-disposable struct enumerator
        var structEnumerType = (_enumerator.ResultType as PointerType)?.ElemType;
        if (structEnumerType != null && !structEnumerType.Inherits(t_IDisposable)) return;

        var exitBlock = loop.Exit.Block;

        Ensure.That(loop.EntryBlock.First is not GuardInst);

        if (exitBlock.Last is not BranchInst { IsJump: true, Then: var succBlock }) {
            throw new UnreachableException();
        }
        exitBlock.SetBranch(new LeaveInst(succBlock));

        var finallyBlock = exitBlock.Method.CreateBlock(exitBlock).SetName("LQ_Finally");

        var builder = new IRBuilder(finallyBlock);

        var disposeFn = t_IDisposable.FindMethod("Dispose");
        
        if (structEnumerType != null) {
            builder.Emit(new CallInst(disposeFn, new[] { _enumerator }, isVirtual: true, constraint: structEnumerType));
        } else {
            var enumer = builder.CreateAsInstance(t_IDisposable, _enumerator);
            var isNotDisposable = builder.CreateEq(enumer, ConstNull.Create());
            builder.Fork(isNotDisposable, (elseBuilder, newBlock) => elseBuilder.CreateCallVirt(disposeFn, enumer));
        }
        builder.Emit(new ResumeInst());

        loop.Header.Block.InsertFirst(new GuardInst(GuardKind.Finally, finallyBlock));
    }
}
internal class IntRangeSource : LinqSourceNode
{
    public IntRangeSource(CallInst subjectCall, LinqStageNode drain)
        : base(drain, default, subjectCall) { }

    Value? _index, _end;

    protected override void EmitHead(LoopBuilder loop, out Value? count, ref LinqStageNode firstStage)
    {
        var start = SubjectCall.Args[0];
        count = SubjectCall.Args[1];

        var builder = loop.PreHeader;

        //if (count < 0 | (sext(start) + sext(count)) > int.MaxValue) throw;
        builder.Throw(
            typeof(ArgumentOutOfRangeException),
            builder.CreateOr(
                builder.CreateSlt(count, ConstInt.CreateI(0)),
                builder.CreateUgt(
                    builder.CreateAdd(
                        builder.CreateConvert(start, PrimType.Int64),
                        builder.CreateConvert(count, PrimType.Int64)),
                    ConstInt.CreateL(int.MaxValue))));

        //int index = phi [PreHeader: start], [Latch: {index + 1}]
        _index = loop.CreateAccum(start, curr => loop.Latch.CreateAdd(curr, ConstInt.CreateI(1))).SetName("lq_rangeidx");
        _end = builder.CreateAdd(start, count);
    }

    protected override Value EmitMoveNext(IRBuilder builder)
        => builder.CreateSlt(_index!, _end!);

    protected override Value EmitCurrent(IRBuilder builder)
        => _index!;
}