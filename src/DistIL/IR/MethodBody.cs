namespace DistIL.IR;

using DistIL.AsmIO;

public class MethodBody
{
    public MethodDef Definition { get; }
    private SymbolTable? _symTable = null;
    private bool _symsDirty = true;

    public ImmutableArray<Argument> Args { get; }
    /// <summary> Gets a view over `Args` excluding the first argument (`this`) if this is an instance method. </summary>
    public ReadOnlySpan<Argument> StaticArgs => Args.AsSpan(Definition.IsStatic ? 0 : 1);

    public TypeDesc ReturnType => Definition.ReturnType;

    /// <summary> The entry block of this method. Should not have predecessors. </summary>
    public BasicBlock EntryBlock { get; private set; } = null!;
    public int NumBlocks { get; private set; } = 0;
    private BasicBlock? _lastBlock; //Last block in the list

    public MethodBody(MethodDef def)
    {
        Definition = def;
        Args = def.Params.Select(p => new Argument(p)).ToImmutableArray();
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
        InvalidateBlocks();
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

    public SymbolTable GetSymbolTable()
    {
        _symTable ??= new();
        if (_symsDirty) {
            _symsDirty = false;
            _symTable.UpdateSlots(this);
        }
        return _symTable;
    }
    internal void InvalidateSlots()
    {
        _symsDirty = true;
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

    public override string ToString() => Definition.ToString();
}