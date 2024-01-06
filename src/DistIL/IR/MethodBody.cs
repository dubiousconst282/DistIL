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

    private LocalSlot? _firstVar, _lastVar;

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

    /// <summary> Creates a local memory slot (variable). </summary>
    public LocalSlot CreateVar(TypeDesc type, string? name = null, bool pinned = false, bool hardExposed = false)
    {
        var slot = new LocalSlot(this, type, pinned, hardExposed);
        IIntrusiveList<MethodBody, LocalSlot>.InsertAfter<VarLinkAccessor>(this, _lastVar, slot);

        if (name != null) {
            GetSymbolTable().SetName(slot, name);
        }
        return slot;
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

    public IEnumerable<LocalSlot> LocalVars()
    {
        for (var slot = _firstVar; slot != null; slot = slot._next) {
            yield return slot;
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
    internal struct VarLinkAccessor : IIntrusiveList<MethodBody, LocalSlot>
    {
        public static ref LocalSlot? First(MethodBody body) => ref body._firstVar;
        public static ref LocalSlot? Last(MethodBody body) => ref body._lastVar;

        public static ref LocalSlot? Prev(LocalSlot slot) => ref slot._prev;
        public static ref LocalSlot? Next(LocalSlot slot) => ref slot._next;
    }
}