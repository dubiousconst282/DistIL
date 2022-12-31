namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal abstract class LinqQuery : LinqStageNode
{
    public MethodBody Method { get; }

    public LinqQuery(CallInst call)
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
    public LinqStageNode Sink { get; }

    protected LinqStageNode(CallInst call, LinqStageNode sink)
        => (SubjectCall, Sink) = (call, sink);

    //Queries are expanded from top to bottom, for example:
    //  Source()                //Top
    //    .Select(MapFn)        //Sink #1
    //    .Where(FilterFn)      //Sink #2
    //    .ToArray();           //Bottom (completed query)
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
        => Sink.EmitHead(builder, estimCount);

    public virtual void EmitBody(IRBuilder builder, Value currItem, BodyLoopData loopData)
        => Sink.EmitBody(builder, currItem, loopData);

    public virtual void EmitTail(IRBuilder builder)
        => Sink.EmitTail(builder);

    public virtual void DeleteSubject()
    {
        SubjectCall?.Remove();
        Sink?.DeleteSubject();
    }
    public LinqQuery GetQuery()
    {
        var node = Sink;
        while (node is not LinqQuery) {
            node = node.Sink;
        }
        return (LinqQuery)node;
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

    protected LinqSourceNode(LinqStageNode sink, UseRef physicalSource, CallInst? subjectCall = null)
        : base(subjectCall!, sink)
    {
        PhysicalSource = physicalSource;
    }

    public virtual void Emit()
    {
        var query = GetQuery();
        var loop = new LoopBuilder(query.SubjectCall.Block);

        EmitHead(loop, out var count);
        Sink.EmitHead(loop.PreHeader, count);
        
        loop.Build(
            emitCond: EmitMoveNext,
            emitBody: body => {
                var currItem = EmitCurrent(body);
                Sink.EmitBody(body, currItem, new BodyLoopData(loop));
            }
        );
        Sink.EmitTail(loop.Exit);
        loop.InsertBefore(query.SubjectCall);
    }

    protected abstract void EmitHead(LoopBuilder loop, out Value? count);
    protected abstract Value EmitMoveNext(IRBuilder builder);
    protected abstract Value EmitCurrent(IRBuilder builder);
}

internal delegate Value LoopAccumVarFactory(Value seed, Func<Value, Value> emitUpdate);