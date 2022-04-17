namespace DistIL.IR;

public abstract class Method : Callsite
{
    private ImmutableArray<BasicBlock> _blocksPO;
    private SlotTracker? _slotTracker = null;
    private bool _slotsDirty = true;

    public List<Argument> Args { get; init; } = null!;
    /// <summary> The entry block of this method. Should not have predecessors. </summary>
    public BasicBlock EntryBlock { get; set; } = null!;
    public int NumBlocks { get; private set; } = 0;
    private BasicBlock? _lastBlock; //Last block in the list

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
    //Called when a block is added/removed, to invalidate cached DFS lists and slots.
    internal void InvalidateBlocks()
    {
        _blocksPO = default;
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

public interface LinkedList<TNode> 
    where TNode : class, LinkedList<TNode>.Node
{
    public TNode? First { get; set; }
    public TNode? Last { get; set; }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    protected static void Remove(LinkedList<TNode> list, TNode node)
    {
        if (node.Prev != null) {
            node.Prev.Next = node.Next;
        } else {
            list.First = node.Next;
        }
        if (node.Next != null) {
            node.Next.Prev = node.Prev;
        } else {
            list.Last = node.Prev;
        }
    }

    /// <summary> Represents an node in a doubly linked list. </summary>
    public interface Node
    {
        TNode? Prev { get; set; }
        TNode? Next { get; set; }
    }

    public struct Enumerator
    {
        TNode? _next, _last;
        public TNode Current { get; private set; }

        public Enumerator(TNode? first, TNode? last)
            => (_next, _last, Current) = (first, last, null!);

        public bool MoveNext()
        {
            if (Current == _last || _next == null) {
                return false;
            }
            Current = _next;
            _next = _next.Next;
            return true;
        }
    }
}

class ItemList : LinkedList<Item>
{
    public Item? First { get; set; }
    public Item? Last { get; set; }

    public void Remove(Item item) => LinkedList<Item>.Remove(this, item);

    public LinkedList<Item>.Enumerator GetEnumerator() => new(First, Last);
}
class Item : LinkedList<Item>.Node
{
    public Item? Prev { get; set; }
    public Item? Next { get; set; }

    static void M(ItemList list)
    {
        foreach (var node in list) {
            list.Remove(node);
            Console.WriteLine(node);
        }
    }
}