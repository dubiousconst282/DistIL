namespace DistIL.IR.Utils;

using DistIL.IR.Intrinsics;

/// <summary> Helper for building a sequence of intructions. </summary>
public class IRBuilder
{
    Instruction? _last;
    BasicBlock _block = null!;

    public BasicBlock Block => _block!;

    /// <summary> Initializes a builder that inserts instructions before `inst`. </summary>
    public IRBuilder(Instruction inst) => SetPosition(inst);
    /// <summary> Initializes a builder that inserts instructions before `inst`, if non null; othewise it appends new instructions before the terminator of `block`. </summary>
    public IRBuilder(BasicBlock block, Instruction? inst = null) => SetPosition(block, inst);

    public virtual void SetPosition(BasicBlock block, Instruction? inst = null)
    {
        _block = block;
        _last = inst;
    }
    public void SetPosition(Instruction inst) => SetPosition(inst.Block, inst);

    public PhiInst CreatePhi(TypeDesc resultType) 
        => _block.InsertPhi(resultType);

    public PhiInst CreatePhi(params PhiArg[] args)
        => _block.InsertPhi(new PhiInst(args));

    public void SetBranch(BasicBlock target) 
        => _block.SetBranch(target);

    public void SetBranch(Value cond, BasicBlock then, BasicBlock else_) 
        => _block.SetBranch(new BranchInst(cond, then, else_));

    public Value CreateBin(BinaryOp op, Value left, Value right)
    {
        return ConstFolding.FoldBinary(op, left, right) ??
               Add(new BinaryInst(op, left, right));
    }

    public Value CreateAdd(Value left, Value right) => CreateBin(BinaryOp.Add, left, right);
    public Value CreateSub(Value left, Value right) => CreateBin(BinaryOp.Sub, left, right);
    public Value CreateMul(Value left, Value right) => CreateBin(BinaryOp.Mul, left, right);
    public Value CreateAnd(Value left, Value right) => CreateBin(BinaryOp.And, left, right);
    public Value CreateOr(Value left, Value right) => CreateBin(BinaryOp.Or, left, right);
    public Value CreateXor(Value left, Value right) => CreateBin(BinaryOp.Xor, left, right);
    public Value CreateShl(Value left, Value right) => CreateBin(BinaryOp.Shl, left, right);
    public Value CreateShra(Value left, Value right) => CreateBin(BinaryOp.Shra, left, right);
    public Value CreateShrl(Value left, Value right) => CreateBin(BinaryOp.Shrl, left, right);

    public Value CreateFAdd(Value left, Value right) => CreateBin(BinaryOp.FAdd, left, right);
    public Value CreateFSub(Value left, Value right) => CreateBin(BinaryOp.FSub, left, right);
    public Value CreateFMul(Value left, Value right) => CreateBin(BinaryOp.FMul, left, right);
    public Value CreateFDiv(Value left, Value right) => CreateBin(BinaryOp.FDiv, left, right);
    public Value CreateFRem(Value left, Value right) => CreateBin(BinaryOp.FRem, left, right);

    public Value CreateCmp(CompareOp op, Value left, Value right)
    {
        return ConstFolding.FoldCompare(op, left, right) ??
               Add(new CompareInst(op, left, right));
    }
    public Value CreateEq(Value left, Value right) => CreateCmp(CompareOp.Eq, left, right);
    public Value CreateNe(Value left, Value right) => CreateCmp(CompareOp.Ne, left, right);
    public Value CreateSlt(Value left, Value right) => CreateCmp(CompareOp.Slt, left, right);
    public Value CreateSgt(Value left, Value right) => CreateCmp(CompareOp.Sgt, left, right);
    public Value CreateSle(Value left, Value right) => CreateCmp(CompareOp.Sle, left, right);
    public Value CreateSge(Value left, Value right) => CreateCmp(CompareOp.Sge, left, right);

    public ConvertInst CreateConvert(Value srcValue, TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
        => Add(new ConvertInst(srcValue, dstType, checkOverflow, srcUnsigned));


    public CallInst CreateCall(MethodDesc method, params Value[] args)
        => Add(new CallInst(method, args));

    public CallInst CreateCallVirt(MethodDesc method, params Value[] args)
        => Add(new CallInst(method, args, true));

    public NewObjInst CreateNewObj(MethodDesc ctor, params Value[] args)
        => Add(new NewObjInst(ctor, args));

    public IntrinsicInst CreateIntrinsic(IntrinsicDesc intrinsic, params Value[] args)
        => Add(new IntrinsicInst(intrinsic, args));

    public LoadFieldInst CreateFieldLoad(FieldDesc field, Value? obj = null)
        => Add(new LoadFieldInst(field, obj));

    public StoreFieldInst CreateFieldStore(FieldDesc field, Value? obj, Value value)
        => Add(new StoreFieldInst(field, obj, value));


    public ArrayLenInst CreateArrayLen(Value array)
        => Add(new ArrayLenInst(array));

    public LoadArrayInst CreateArrayLoad(Value array, Value index, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new LoadArrayInst(array, index, elemType ?? (array.ResultType as ArrayType)!.ElemType, flags));

    public StoreArrayInst CreateArrayStore(Value array, Value index, Value value, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new StoreArrayInst(array, index, value, elemType ?? (array.ResultType as ArrayType)!.ElemType, flags));

    public IntrinsicInst CreateNewArray(TypeDesc elemType, Value length)
        => Add(new IntrinsicInst(CilIntrinsic.NewArray, elemType, length));


    public void CreateMarker(string text)
        => Add(new IntrinsicInst(IRIntrinsic.Marker, ConstString.Create(text)));

    /// <summary> Adds the specified instruction at the current position. </summary>
    public TInst Add<TInst>(TInst inst) where TInst : Instruction
    {
        Append(inst);
        return inst;
    }

    protected virtual void Append(Instruction inst)
    {
        if (_last != null) {
            inst.InsertAfter(_last);
        } else {
            _block!.InsertAnteLast(inst);
        }
        _last = inst;
    }
}