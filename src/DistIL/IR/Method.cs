namespace DistIL.IR;

public abstract class Method : Callsite
{
    private List<BasicBlock> _blocks = new();
    private ImmutableArray<BasicBlock> _blocksPO;
    private SlotTracker? _slotTracker = null;
    private bool _slotsDirty = true;

    public List<Argument> Args { get; init; } = null!;
    /// <summary> The entry block of this method. Should not have predecessors. </summary>
    public BasicBlock EntryBlock { get; set; } = null!;

    /// <summary> Creates and adds an empty block to this method. If the method is empty, this block will be set as the entry block. </summary>
    public BasicBlock CreateBlock()
    {
        var block = new BasicBlock(this);
        _blocks.Add(block);
        EntryBlock ??= block;

        _blocksPO = default;
        return block;
    }

    public bool RemoveBlock(BasicBlock block)
    {
        Assert(block.Method == this && block != EntryBlock);

        if (_blocks.Remove(block)) {
            InvalidateSlots();
            _blocksPO = default;
            return true;
        }
        return false;
    }

    //TODO: abstract list away and allow removals during iterations 
    public List<BasicBlock> GetBlocks()
    {
        return _blocks;
    }

    /// <summary> Returns an array containing the method's blocks in DFS post order. </summary>
    public ImmutableArray<BasicBlock> GetBlocksPostOrder()
    {
        if (_blocksPO.IsDefault) {
            var blocks = ImmutableArray.CreateBuilder<BasicBlock>();

            GraphTraversal.DepthFirst(
                entry: EntryBlock,
                getChildren: b => b.Succs,
                postVisit: blocks.Add
            );
            _blocksPO = blocks.ToImmutable();
        }
        return _blocksPO;
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
        _slotTracker?.Clear();
        _slotsDirty = true;
    }

    public IEnumerator<BasicBlock> GetEnumerator()
    {
        return _blocks.GetEnumerator();
    }

    public IEnumerable<Instruction> Instructions()
    {
        foreach (var block in this) {
            foreach (var inst in block) {
                yield return inst;
            }
        }
    }
}