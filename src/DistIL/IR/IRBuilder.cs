namespace DistIL.IR;

/// <summary>
/// Helper for building a sequence of intructions. This class has two modes of operation:  <br/>
/// - Immediate: Instructions are created and added immediately after `Position`.  <br/>
/// - Delayed: Instructions are created and stored internally, and can be added later by `MoveBefore()`/`MoveAfter()` or `PrependInto()`/`AppendInto()`.
/// </summary>
public class IRBuilder
{
    Instruction _first = null!, _last = null!;
    bool _delayed = false;
    BasicBlock? _block;

    /// <summary> The instruction where new instructions are to be added after. </summary>
    public Instruction? Position {
        get => _last;
        set {
            Ensure(!_delayed && value != null);
            _last = value!;
        }
    }
    public BasicBlock Block => _block ?? _last.Block;

    /// <summary> Initializes a new delayed or immediate. </summary>
    public IRBuilder(bool delayed = false) { _delayed = delayed; }
    /// <summary> Initializes a new immediate IRBuilder. </summary>
    public IRBuilder(Instruction pos) { Position = pos; }
    /// <summary> Initializes a new immediate IRBuilder. </summary>
    public IRBuilder(BasicBlock pos) { _block = pos; }

    public PhiInst CreatePhi(TypeDesc resultType)
        => Block.AddPhi(new PhiInst(resultType));

    public PhiInst CreatePhi(params PhiArg[] args)
        => Block.AddPhi(new PhiInst(args));

    public BranchInst SetBranch(Value cond, BasicBlock then, BasicBlock else_)
        => SetBranch(new BranchInst(cond, then, else_));

    public BranchInst SetBranch(BasicBlock target)
        => SetBranch(new BranchInst(target));

    public BranchInst SetBranch(BranchInst branch)
    {
        Block.SetBranch(branch);
        return branch;
    }

    public BinaryInst CreateBin(BinaryOp op, Value left, Value right)
        => Add(new BinaryInst(op, left, right));

    public CompareInst CreateCmp(CompareOp op, Value left, Value right)
        => Add(new CompareInst(op, left, right));

    public ConvertInst CreateConvert(Value srcValue, TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
        => Add(new ConvertInst(srcValue, dstType, checkOverflow, srcUnsigned));

    public ArrayLenInst CreateArrayLen(Value array)
        => Add(new ArrayLenInst(array));

    public LoadArrayInst CreateArrayLoad(Value array, Value index, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new LoadArrayInst(array, index, elemType ?? (array.ResultType as ArrayType)!.ElemType, flags));

    public StoreArrayInst CreateArrayStore(Value array, Value index, Value value, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new StoreArrayInst(array, index, value, elemType ?? (array.ResultType as ArrayType)!.ElemType, flags));

    public IntrinsicInst CreateNewArray(TypeDesc elemType, Value length)
        => Add(new IntrinsicInst(IntrinsicId.NewArray, new ArrayType(elemType), length));

    public LoadFieldInst CreateFieldLoad(FieldDesc field, Value? obj = null)
        => Add(new LoadFieldInst(field, obj));

    public StoreFieldInst CreateFieldStore(FieldDesc field, Value? obj, Value value)
        => Add(new StoreFieldInst(field, obj, value));

    public CallInst CreateCall(MethodDesc method, params Value[] args)
        => Add(new CallInst(method, args));

    public CallInst CreateVirtualCall(MethodDesc method, params Value[] args)
        => Add(new CallInst(method, args, true));

    public BinaryInst CreateAdd(Value left, Value right) => CreateBin(BinaryOp.Add, left, right);
    public BinaryInst CreateSub(Value left, Value right) => CreateBin(BinaryOp.Sub, left, right);
    public BinaryInst CreateMul(Value left, Value right) => CreateBin(BinaryOp.Mul, left, right);
    public BinaryInst CreateShl(Value left, Value right) => CreateBin(BinaryOp.Shl, left, right);
    public BinaryInst CreateShra(Value left, Value right) => CreateBin(BinaryOp.Shra, left, right);
    public BinaryInst CreateShrl(Value left, Value right) => CreateBin(BinaryOp.Shrl, left, right);

    public CompareInst CreateEq(Value left, Value right) => CreateCmp(CompareOp.Eq, left, right);
    public CompareInst CreateNe(Value left, Value right) => CreateCmp(CompareOp.Ne, left, right);
    public CompareInst CreateSlt(Value left, Value right) => CreateCmp(CompareOp.Slt, left, right);

    public void CreateMarker(string text)
        => Add(new IntrinsicInst(IntrinsicId.Marker, PrimType.Void, ConstString.Create(text)));

    /// <summary> Adds the specified instruction into the basic block. </summary>
    public TInst Add<TInst>(TInst inst) where TInst : Instruction
    {
        if (_delayed) {
            _first ??= inst;
            inst.Prev = _last;
            if (_last != null) {
                _last.Next = inst;
            }
        } else if (_last != null) {
            inst.InsertAfter(_last);
        } else {
            Ensure(_block != null, "Cannot add new instructions when position is unset in immediate IRBuilder");
            _block.InsertLast(inst);
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