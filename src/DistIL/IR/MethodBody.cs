namespace DistIL.IR;

public class MethodBody
{
    public MethodDef Definition { get; }
    private SymbolTable? _symTable = null;

    public ImmutableArray<Argument> Args { get; }
    public TypeDesc ReturnType => Definition.ReturnType;

    /// <summary> The entry block of this method. Should not have predecessors. </summary>
    public BasicBlock EntryBlock { get; private set; } = null!;
    public int NumBlocks { get; private set; } = 0;
    private BasicBlock? _lastBlock; //Last block in the list

    public MethodBody(MethodDef def)
    {
        Definition = def;
        Args = def.Params.Select((p, i) => new Argument(p, i)).ToImmutableArray();
    }

    /// <summary> Creates and adds an empty block to this method. If the method is empty, this block will be set as the entry block. </summary>
    public BasicBlock CreateBlock(BasicBlock? insertAfter = null)
    {
        var block = new BasicBlock(this);

        if (EntryBlock == null) {
            EntryBlock = _lastBlock = block;
        } else {
            InsertBlock(insertAfter ?? _lastBlock!, block);
        }
        NumBlocks++;
        return block;
    }

    private void InsertBlock(BasicBlock after, BasicBlock block)
    {
        block.Prev = after;
        block.Next = after.Next;

        if (after.Next != null) {
            after.Next.Prev = block;
        } else {
            _lastBlock = block;
        }
        after.Next = block;
    }

    /// <summary> Removes a block from the method without cleanup. </summary>
    internal bool RemoveBlock(BasicBlock block)
    {
        Ensure.That(block.Method == this && block != EntryBlock);

        block.Prev!.Next = block.Next;

        if (block.Next != null) {
            block.Next.Prev = block.Prev;
        } else {
            _lastBlock = block.Prev;
        }
        block.Method = null!; //to ensure it can't be removed again
        NumBlocks--;
        return false;
    }

    public SymbolTable GetSymbolTable()
    {
        //TODO: Context options and SymbolTable.ForceSeqNames
        return _symTable ??= new(this, true);
    }

    /// <summary> Performs a depth-first traversal over this method's control flow graph, starting from the entry block and visiting only reachable blocks. </summary>
    public void TraverseDepthFirst(Action<BasicBlock>? preVisit = null, Action<BasicBlock>? postVisit = null)
    {
        var pending = new ArrayStack<(BasicBlock Block, BasicBlock.SuccIterator Itr)>();
        var visited = new RefSet<BasicBlock>();

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
}