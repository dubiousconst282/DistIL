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

    public void PrependInto(BasicBlock block)
    {
        if (_first != null) {
            block.InsertRange(null, _first, _last);
        }
    }
    public void AppendInto(BasicBlock block)
    {
        if (_first != null) {
            block.InsertRange(block.Last, _first, _last);
        }
    }

    public void MoveBefore(Instruction inst)
    {
        if (_first != null) {
            inst.Block.InsertRange(inst.Prev, _first, _last);
        }
    }
    public void MoveAfter(Instruction inst)
    {
        if (_first != null) {
            inst.Block.InsertRange(inst, _first, _last);
        }
    }
}