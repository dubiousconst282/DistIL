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
    public abstract override void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData);
}

internal abstract class LinqStageNode
{
    public CallInst SubjectCall { get; }
    public LinqStageNode Sink { get; }

    protected LinqStageNode(CallInst call, LinqStageNode sink)
        => (SubjectCall, Sink) = (call, sink);

    //Queries are expanded from front to back, for example:
    //  ArraySource()           //Source
    //    .Select(MapFn)        //Sink #1
    //    .Where(FilterFn)      //Sink #2
    //    .ToArray();           //Sink #3 (LinqQuery)
    //
    //Will be rewritten in this way:
    //  Head();
    //  foreach (var item in Source())  //Loop created by Source()
    //    var item2 = MapFn(item);
    //    if (!FilterFn(item2)) goto skipBlock;
    //    Body(item2);
    //  Tail();
    public virtual void EmitHead(IRBuilder builder, Value? estimCount)
        => Sink.EmitHead(builder, estimCount);

    public virtual void EmitBody(IRBuilder builder, Value currItem, in BodyLoopData loopData)
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

    public struct BodyLoopData
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
        EmitHead(loop.PreHeader);

        var maxCount = EmitSourceCount(loop.PreHeader);
        Sink.EmitHead(loop.PreHeader, maxCount);

        var index = loop.CreateInductor().SetName("lq_index");
        
        loop.Build(
            emitCond: header => EmitMoveNext(header, index),
            emitBody: body => {
                var currItem = EmitCurrent(body, index, loop.Latch.Block);
                Sink.EmitBody(body, currItem, new BodyLoopData(loop));
            }
        );
        Sink.EmitTail(loop.Exit);
        loop.InsertBefore(query.SubjectCall);
    }

    protected abstract Value EmitMoveNext(IRBuilder builder, Value currIndex);
    protected abstract Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock);

    protected virtual void EmitHead(IRBuilder builder) { }
    protected virtual Value? EmitSourceCount(IRBuilder builder) => null;
}

internal delegate Value LoopAccumVarFactory(Value seed, Func<Value, Value> emitUpdate);