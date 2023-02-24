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

    public override void EmitHead(IRBuilder builder, Value? estimCount) { }
    public override void EmitTail(IRBuilder builder) { }
    public abstract override void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData);
}

internal abstract class LinqStageNode
{
    public CallInst SubjectCall { get; }
    public LinqStageNode Drain { get; }

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
    public virtual void EmitHead(IRBuilder builder, Value? estimCount)
        => Drain.EmitHead(builder, estimCount);

    public virtual void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
        => Drain.EmitBody(builder, currItem, loopData);

    public virtual void EmitTail(IRBuilder builder)
        => Drain.EmitTail(builder);

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

    public record BodyLoopData
    {
        public BasicBlock SkipBlock;
        public IRBuilder PreHeader, Header, Exit;
        public LoopAccumVarFactory CreateAccum;

        public BodyLoopData(LoopBuilder loop)
        {
            SkipBlock = loop.Latch.Block;
            PreHeader = loop.PreHeader;
            Header = loop.Header;
            Exit = loop.Exit;
            CreateAccum = loop.CreateAccum;
        }
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

    public virtual void Emit()
    {
        var sink = GetSink();
        var loop = new LoopBuilder(sink.SubjectCall.Block);

        EmitHead(loop, out var count);
        Drain.EmitHead(loop.PreHeader, count);
        
        loop.Build(
            emitCond: EmitMoveNext,
            emitBody: body => {
                var currItem = EmitCurrent(body);
                Drain.EmitBody(body, currItem, new BodyLoopData(loop));
            }
        );
        Drain.EmitTail(loop.Exit);
        loop.InsertBefore(sink.SubjectCall);
    }

    protected abstract void EmitHead(LoopBuilder loop, out Value? count);
    protected abstract Value EmitMoveNext(IRBuilder builder);
    protected abstract Value EmitCurrent(IRBuilder builder);
}

internal delegate Value LoopAccumVarFactory(Value seed, Func<Value, Value> emitUpdate);