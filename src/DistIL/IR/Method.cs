namespace DistIL.IR;

public abstract class Method : Callsite
{
    private SlotTracker? _slotTracker = null;
    private bool _slotsDirty = true;

    public List<Argument> Args { get; init; } = null!;
    /// <summary> Gets a view over `Args` excluding the first argument (`this`) if this is an instance method. </summary>
    public ReadOnlySpan<Argument> StaticArgs => Args.AsSpan(IsStatic ? 0 : 1);

    /// <summary> The entry block of this method. Should not have predecessors. </summary>
    public BasicBlock EntryBlock { get; private set; } = null!;
    public int NumBlocks { get; private set; } = 0;
    private BasicBlock? _lastBlock; //Last block in the list

    /// <summary> Whether IR is available for this method (was sucessfully imported). </summary>
    public bool CodeAvailable => EntryBlock != null;

    /// <summary> Creates and adds an empty block to this method. If the method is empty, this block will be set as the entry block. </summary>
    public BasicBlock CreateBlock()
    {
        var block = new BasicBlock(this);

        EntryBlock ??= block;

        if (_lastBlock != null) {
            block.Prev = _lastBlock;
            _lastBlock.Next = block;
        }
        _lastBlock = block;

        NumBlocks++;
        InvalidateBlocks();
        return block;
    }

    public bool RemoveBlock(BasicBlock block)
    {
        Ensure(block.Method == this && block != EntryBlock);

        block.Prev!.Next = block.Next;

        if (block.Next != null) {
            block.Next.Prev = block.Prev;
        } else {
            _lastBlock = block.Prev;
        }
        block.Method = null!; //to ensure it can't be removed again
        NumBlocks--;
        InvalidateBlocks();
        return false;
    }

    public void ClearBlocks()
    {
        EntryBlock = _lastBlock = null!;
        NumBlocks = 0;
        InvalidateBlocks();
    }

    public SlotTracker GetSlotTracker()
    {
        _slotTracker ??= new();
        if (_slotsDirty) {
            _slotsDirty = false;
            _slotTracker.Update(this);
        }
        return _slotTracker;
    }
    public void InvalidateSlots()
    {
        _slotsDirty = true;
    }
    //Called when a block is added/removed, to invalidate cached DFS lists and slots.
    internal void InvalidateBlocks()
    {
        InvalidateSlots();
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
}