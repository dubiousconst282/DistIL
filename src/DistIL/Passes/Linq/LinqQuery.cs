namespace DistIL.Passes.Linq;

using DistIL.IR.Utils;

internal abstract class LinqQuery
{
    public CallInst SubjectCall { get; }
    public LinqStageNode Pipeline { get; }

    public MethodBody Method { get; }
    private BasicBlock? _lastBlock;

    public LinqQuery(CallInst call, LinqStageNode pipeline)
    {
        SubjectCall = call;
        Pipeline = pipeline;
        Method = call.Block.Method;
    }
    
    public abstract bool Emit();

    protected IRBuilder NewBlock(string? name = null, BasicBlock? insertAfter = null)
    {
        var block = Method.CreateBlock(insertAfter ?? _lastBlock ?? SubjectCall.Block);
        _lastBlock = block;
        if (name != null) {
            block.SetName("LQ_" + name);
        }
        return new IRBuilder(block);
    }
}

internal abstract class LinqStageNode
{
    public CallInst? SubjectCall { get; }
    public LinqStageNode? Source { get; }

    protected LinqStageNode() { }

    protected LinqStageNode(CallInst? call, LinqStageNode? source)
        => (SubjectCall, Source) = (call, source);

    public abstract Value EmitMoveNext(IRBuilder builder, Value currIndex);
    public abstract Value EmitCurrent(IRBuilder builder, Value currIndex, BasicBlock skipBlock);

    public virtual void EmitHead(IRBuilder builder) => Source?.EmitHead(builder);
    public virtual Value? EmitSourceCount(IRBuilder builder) => Source?.EmitSourceCount(builder);

    public virtual void DeleteSubject()
    {
        SubjectCall?.Remove();
        Source?.DeleteSubject();
    }
}