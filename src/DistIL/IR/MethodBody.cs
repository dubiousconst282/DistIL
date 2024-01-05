namespace DistIL.IR;

public class MethodBody
{
    public MethodDef Definition { get; }
    private SymbolTable? _symTable = null;

    public ImmutableArray<Argument> Args { get; }
    public TypeDesc ReturnType => Definition.ReturnType;

    /// <summary> The entry block of this method. Should not have predecessors. </summary>
    public BasicBlock EntryBlock => _firstBlock!;
    public int NumBlocks { get; private set; } = 0;
    private BasicBlock? _firstBlock, _lastBlock;

    public MethodBody(MethodDef def)
    {
        Definition = def;
        Args = def.Params.Select((p, i) => new Argument(p, i)).ToImmutableArray();
    }

    /// <summary> Creates and adds an empty block to this method. If the method is empty, this block will be set as the entry block. </summary>
    public BasicBlock CreateBlock(BasicBlock? insertAfter = null)
    {
        var block = new BasicBlock(this);

        IIntrusiveList<MethodBody, BasicBlock>.InsertAfter<BlockLinkAccessor>(this, insertAfter ?? _lastBlock, block);
        NumBlocks++;

        return block;
    }

    /// <summary> Removes a block from the method without cleanup. </summary>
    internal bool RemoveBlock(BasicBlock block)
    {
        Debug.Assert(block.Method == this);
        Ensure.That(block != EntryBlock);

        IIntrusiveList<MethodBody, BasicBlock>.RemoveRange<BlockLinkAccessor>(this, block, block);

        block.Method = null!; // prevent multiple remove calls
        NumBlocks--;
        return false;
    }

    public SymbolTable GetSymbolTable()
    {
        return _symTable ??= new(this);
    }

    /// <summary> Performs a depth-first traversal over this method's control flow graph, starting from the entry block and visiting only reachable blocks. </summary>
    public void TraverseDepthFirst(Action<BasicBlock>? preVisit = null, Action<BasicBlock>? postVisit = null)
    {
        var pending = new ArrayStack<(BasicBlock Block, BasicBlock.SuccIterator Itr)>();
        var visited = new RefSet<BasicBlock>(NumBlocks);

        Push(EntryBlock);

        while (!pending.IsEmpty) {
            ref var top = ref pending.Top;

            if (top.Itr.MoveNext()) {
                Push(top.Itr.Current);
            } else {
                postVisit?.Invoke(top.Block);
                pending.Pop();
            }
        }

        void Push(BasicBlock block)
        {
            if (visited.Add(block)) {
                pending.Push((block, block.Succs));
                preVisit?.Invoke(block);
            }
        }
    }

    public IEnumerator<BasicBlock> GetEnumerator()
    {
        for (var block = EntryBlock; block != null; block = block.Next) {
            yield return block;
        }
    }

    public IEnumerable<Instruction> Instructions()
    {
        for (var block = EntryBlock; block != null; block = block.Next) {
            foreach (var inst in block) {
                yield return inst;
            }
        }
    }

    public override string ToString() => Definition.ToString();


    internal struct BlockLinkAccessor : IIntrusiveList<MethodBody, BasicBlock>
    {
        public static ref BasicBlock? First(MethodBody body) => ref body._firstBlock;
        public static ref BasicBlock? Last(MethodBody body) => ref body._lastBlock;

        public static ref BasicBlock? Prev(BasicBlock block) => ref block._prev;
        public static ref BasicBlock? Next(BasicBlock block) => ref block._next;
    }
}