namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal abstract class LinqSink : LinqStageNode
{
    public MethodBody Method { get; }

    public LinqSink(CallInst call)
        : base(call, null!)
    {
        Method = call.Block.Method;
    }

    public virtual void EmitHead(IRBuilder builder, EstimatedSourceLen estimLen) { }
    public virtual void EmitTail(IRBuilder builder) { }
    public abstract override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData);
}

internal abstract class LinqStageNode
{
    public CallInst SubjectCall { get; }
    public LinqStageNode Drain { get; }

    /// <summary> Whether this stage may discard source items. </summary>
    public virtual bool IsFiltering => false;
    /// <summary> Whether this stage may drain more items than given by the source. </summary>
    public virtual bool IsGenerating => false;

    protected LinqStageNode(CallInst call, LinqStageNode drain)
        => (SubjectCall, Drain) = (call, drain);

    //Queries are expanded from front-to-back, for example:
    //  Source()                //Front
    //    .Select(MapFn)        //Drain #1
    //    .Where(FilterFn)      //Drain #2
    //    .ToArray();           //Sink
    //
    //Will be rewritten in this way:
    //  Head();
    //  foreach (var item in Source()) { //Loop created by Source(), item propagated down through the chain
    //    var item2 = MapFn(item);
    //    if (!FilterFn(item2)) goto SkipBlock;
    //    Body(item2);
    //SkipBlock:
    //  }
    //Exit:
    //  Tail();
    public virtual void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
        => Drain.EmitBody(builder, currItem, loopData);

    public virtual void DeleteSubject()
    {
        if (SubjectCall is { NumUses: < 2 }) {
            SubjectCall.Remove();
        }
        Drain?.DeleteSubject();
    }
    public LinqSink GetSink()
    {
        var node = Drain;
        while (node is not LinqSink) {
            node = node.Drain;
        }
        return (LinqSink)node;
    }
}
internal abstract class LinqSourceNode : LinqStageNode
{
    public UseRef PhysicalSource { get; }

    protected LinqSourceNode(LinqStageNode drain, UseRef physicalSource, CallInst? subjectCall = null)
        : base(subjectCall!, drain)
    {
        PhysicalSource = physicalSource;
    }

    public void Emit()
    {
        var sink = GetSink();

        var loop = new LoopBuilder(sink.SubjectCall.Block, "LQ_");
        var firstStage = Drain;

        EmitHead(loop, out var length, ref firstStage);
        sink.EmitHead(loop.PreHeader, new EstimatedSourceLen(length, firstStage));

        loop.Build(
            emitCond: EmitMoveNext,
            emitBody: body => firstStage.EmitBody(body, EmitCurrent(body), new BodyLoopData(loop))
        );
        sink.EmitTail(loop.Exit);

        if (sink is not LoopSink) {
            loop.InsertBefore(sink.SubjectCall);
        }
    }

    protected static void IntegrateSkipTakeRanges(IRBuilder builder, ref LinqStageNode firstStage, out Value? offset, ref Value length)
    {
        offset = null;

        if (firstStage is SkipStage { SubjectCall.Args: [_, var skipCount] }) {
            //It's important to clamp skipCount both ways to avoid creating GC tracking holes.
            //  offset = clamp(skipCount, 0, (int)count)
            //  startPtr += offset
            //  count -= offset
            offset = builder.CreateMin(
                builder.CreateMax(skipCount, ConstInt.CreateI(0)),
                builder.CreateConvert(length, PrimType.Int32));
            length = builder.CreateSub(length, offset);
            firstStage = firstStage.Drain;
        }
        if (firstStage is TakeStage { SubjectCall.Args: [_, var takeCount] }) {
            //Take() is easier, we can exploit twos-complement by using an unsigned min() to clamp between 0..count.
            //  count = min(count, (uint)takeCount)
            length = builder.CreateMin(
                builder.CreateConvert(length, PrimType.Int32),
                takeCount, unsigned: true);
            firstStage = firstStage.Drain;
        }
    }

    protected abstract void EmitHead(LoopBuilder loop, out Value? length, ref LinqStageNode firstStage);
    protected abstract Value EmitMoveNext(IRBuilder builder);
    protected abstract Value EmitCurrent(IRBuilder builder);
}

internal record BodyLoopData
{
    public LoopBuilder SourceLoop;
    public BasicBlock SkipBlock;
    public LoopAccumVarFactory CreateAccum;

    public IRBuilder PreHeader => SourceLoop.PreHeader;
    public IRBuilder Header => SourceLoop.Header;
    public IRBuilder Exit => SourceLoop.Exit;

    public BodyLoopData(LoopBuilder loop)
    {
        SourceLoop = loop;
        SkipBlock = loop.Latch.Block;
        CreateAccum = loop.CreateAccum;
    }
}
internal delegate Value LoopAccumVarFactory(Value seed, Func<Value, Value> emitUpdate);

internal struct EstimatedSourceLen
{
    public Value? Length { get; }
    public bool IsUnderEstimation { get; }
    public bool IsOverEstimation { get; }

    [MemberNotNullWhen(true, nameof(Length))]
    public bool IsExact => Length != null && !IsUnderEstimation && !IsOverEstimation;

    public EstimatedSourceLen(Value? length, LinqStageNode entryStage)
    {
        Length = length;

        if (length == null) return;

        for (var node = entryStage; node != null; node = node.Drain) {
            IsOverEstimation |= node.IsFiltering;
            IsUnderEstimation |= node.IsGenerating;
        }
    }
}