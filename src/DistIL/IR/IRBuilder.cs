namespace DistIL.IR;

/// <summary>
/// Helper for building a sequence of intructions. This class has two modes of operation:  <br/>
/// - Immediate: Instructions are created and added immediately after `Position`.  <br/>
/// - Delayed: Instructions are created and stored internally, and can be added later by `MoveBefore()`/`MoveAfter()` or `PrependInto()`/`AppendInto()`.
/// </summary>
public class IRBuilder
{
    private Instruction _first = null!, _last = null!;
    private bool _delayed = false;
    /// <summary> The instruction where new instructions are to be added after. </summary>
    public Instruction Position {
        get => _last;
        set {
            Ensure(!_delayed, "Cannot set position of delayed IRBuilder");
            _last = value;
        }
    }

    /// <summary> Initializes a new delayed or immediate. </summary>
    public IRBuilder(bool delayed = false) { _delayed = delayed; }
    /// <summary> Initializes a new immediate IRBuilder. </summary>
    public IRBuilder(Instruction pos) { Position = pos; }

    public BinaryInst CreateBin(BinaryOp op, Value left, Value right)
        => Add(new BinaryInst(op, left, right));

    public BinaryInst CreateAdd(Value left, Value right) => CreateBin(BinaryOp.Add, left, right);
    public BinaryInst CreateSub(Value left, Value right) => CreateBin(BinaryOp.Sub, left, right);
    public BinaryInst CreateMul(Value left, Value right) => CreateBin(BinaryOp.Mul, left, right);
    public BinaryInst CreateShl(Value left, Value right) => CreateBin(BinaryOp.Shl, left, right);
    public BinaryInst CreateShra(Value left, Value right) => CreateBin(BinaryOp.Shra, left, right);
    public BinaryInst CreateShrl(Value left, Value right) => CreateBin(BinaryOp.Shrl, left, right);

    /// <summary> Adds the specified instruction into the basic block. </summary>
    public TInst Add<TInst>(TInst inst) where TInst : Instruction
    {
        if (_delayed) {
            _first ??= inst;
            inst.Prev = _last;
            if (_last != null) {
                _last.Next = inst;
            }
        } else {
            Ensure(_last != null, "Cannot add new instructions when position is unset in immediate IRBuilder");
            inst.InsertAfter(_last);
        }
        _last = inst;
        return inst;
    }

    /// <summary> Resets this delayed builder. </summary>
    public void Clear()
    {
        Ensure(_delayed, "Cannot clear immediate IRBuilder");
        _first = _last = null!;
    }

    public void PrependInto(BasicBlock block) => MoveBefore(block, block.First);
    public void AppendInto(BasicBlock block) => MoveAfter(block, block.Last);

    public void MoveBefore(Instruction inst) => MoveBefore(inst.Block, inst);
    public void MoveAfter(Instruction inst) => MoveAfter(inst.Block, inst);

    public void MoveBefore(BasicBlock block, Instruction? pos)
    {
        if (_first == null) return;

        if (pos != null) {
            _last.Next = pos;
            _first.Prev = pos.Prev;

            if (pos.Prev != null) {
                pos.Prev.Next = _first;
            } else {
                block.First = _first;
            }
            pos.Prev = _last;
        } else {
            block.First = _first;
        }
        block.Last ??= _last;
    }

    public void MoveAfter(BasicBlock block, Instruction? pos)
    {
        if (_first == null) return;
        TransferTo(block);

        if (pos != null) {
            _first.Prev = pos;
            _last.Next = pos.Next;

            if (pos.Next != null) {
                pos.Next.Prev = _last;
            } else {
                block.Last = _last;
            }
            pos.Next = _first;
        } else {
            block.Last = _last;
        }
        block.First ??= _first;
    }

    private void TransferTo(BasicBlock block)
    {
        for (var inst = _first; inst != null; inst = inst.Next) {
            Assert(inst.Block == null, "IRBuilder instruction is already owned by a block!");
            inst.Block = block;
        }
    }
}