namespace DistIL.IR.Utils;

using DistIL.IR.Intrinsics;

/// <summary> Helper for building a sequence of intructions. </summary>
public class IRBuilder
{
    Instruction? _last;
    BasicBlock _block = null!;
    InsertionDir _initialDir = InsertionDir._Invalid;

    public BasicBlock Block => _block!;
    public MethodBody Method => _block!.Method;
    public ModuleResolver Resolver => Method.Definition.Module.Resolver;

    /// <summary> Initializes a builder that inserts instructions relative to <paramref name="inst"/>. </summary>
    public IRBuilder(Instruction inst, InsertionDir dir) => SetPosition(inst, dir);

    /// <summary> Initializes a builder that inserts instructions relative to <paramref name="block"/>. </summary>
    public IRBuilder(BasicBlock block, InsertionDir dir = InsertionDir.BeforeLast) => SetPosition(block, dir);

    public void SetPosition(BasicBlock block, InsertionDir dir = InsertionDir.BeforeLast)
    {
        _block = block;
        _last = null;
        _initialDir = InsertionDir._Invalid;

        if (dir == InsertionDir.Before) {
            _last = block.FirstNonHeader.Prev;
        } else if (dir == InsertionDir.BeforeLast && block.Last != null) {
            Ensure.That(block.Last.IsBranch, "Malformed block");
            SetInitialPosBefore(block.Last);
        } else {
            //Block is either empty, or dir == After
            _initialDir = InsertionDir.After;
        }
    }
    public void SetPosition(Instruction inst, InsertionDir dir)
    {
        _block = inst.Block;
        _initialDir = InsertionDir._Invalid;

        if (dir == InsertionDir.Before) {
            SetInitialPosBefore(inst);
        } else {
            Ensure.That(dir == InsertionDir.After, "Invalid direction");
            _last = inst;
        }
    }
    private void SetInitialPosBefore(Instruction inst)
    {
        _last = inst.Prev;
        _initialDir = _last == null ? InsertionDir.Before : InsertionDir._Invalid;
    }

    public PhiInst CreatePhi(TypeDesc type) 
        => _block.InsertPhi(type);

    public PhiInst CreatePhi(TypeDesc type, params PhiArg[] args)
        => _block.InsertPhi(new PhiInst(type, args));

    public void SetBranch(BasicBlock target) 
        => _block.SetBranch(target);

    public void SetBranch(Value cond, BasicBlock then, BasicBlock else_) 
        => _block.SetBranch(new BranchInst(cond, then, else_));

    /// <summary>
    /// Terminates the current block with a new branch <c>goto cond ? newBlock : elseBlock</c>,
    /// and sets the builder position to the start of the new block.
    /// </summary>
    public void Fork(Value cond, BasicBlock elseBlock)
    {
        var newBlock = Method.CreateBlock(insertAfter: Block);
        SetBranch(cond, newBlock, elseBlock);
        SetPosition(newBlock);
    }

    public void Fork(Action<IRBuilder, BasicBlock> emitTerminator)
    {
        var newBlock = Method.CreateBlock(insertAfter: Block);
        emitTerminator(this, newBlock);
        SetPosition(newBlock);
    }

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

    /// <summary> Searches for <paramref name="methodName"/> in the instance object type (first argument), and creates a callvirt instruction for it. </summary>
    public CallInst CreateCallVirt(string methodName, params Value[] args)
    {
        var instanceType = args[0].ResultType;

        if (instanceType is ByrefType refType) {
            instanceType = refType.ElemType;
            Ensure.That(instanceType.IsValueType);
        }
        if (instanceType is PrimType prim) {
            instanceType = prim.GetDefinition(Resolver);
        }
        var method = instanceType.FindMethod(methodName, searchBaseAndItfs: true);
        return CreateCallVirt(method, args);
    }

    public NewObjInst CreateNewObj(MethodDesc ctor, params Value[] args)
        => Add(new NewObjInst(ctor, args));

    public IntrinsicInst CreateIntrinsic(IntrinsicDesc intrinsic, params Value[] args)
        => Add(new IntrinsicInst(intrinsic, args));


    public LoadFieldInst CreateFieldLoad(FieldDesc field, Value? obj = null)
        => Add(new LoadFieldInst(field, obj));

    public StoreFieldInst CreateFieldStore(FieldDesc field, Value? obj, Value value)
        => Add(new StoreFieldInst(field, obj, value));

    public FieldAddrInst CreateFieldAddr(FieldDesc field, Value? obj)
        => Add(new FieldAddrInst(field, obj));


    public LoadFieldInst CreateFieldLoad(string fieldName, Value obj)
        => Add(new LoadFieldInst(obj.ResultType.FindField(fieldName), obj));


    public LoadVarInst CreateVarLoad(Variable var)
        => Add(new LoadVarInst(var));

    public StoreVarInst CreateVarStore(Variable var, Value value)
        => Add(new StoreVarInst(var, value));

    public VarAddrInst CreateVarAddr(Variable var)
        => Add(new VarAddrInst(var));


    public ArrayLenInst CreateArrayLen(Value array)
        => Add(new ArrayLenInst(array));

    public LoadArrayInst CreateArrayLoad(Value array, Value index, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new LoadArrayInst(array, index, elemType ?? ((ArrayType)array.ResultType).ElemType, flags));

    public StoreArrayInst CreateArrayStore(Value array, Value index, Value value, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new StoreArrayInst(array, index, value, elemType ?? ((ArrayType)array.ResultType).ElemType, flags));

    public ArrayAddrInst CreateArrayAddr(Value array, Value index, TypeDesc? elemType = null, ArrayAccessFlags flags = default)
        => Add(new ArrayAddrInst(array, index, elemType ?? ((ArrayType)array.ResultType).ElemType, flags));


    public LoadPtrInst CreatePtrLoad(Value addr, TypeDesc? elemType = null, PointerFlags flags = default)
        => Add(new LoadPtrInst(addr, elemType ?? ((PointerType)addr.ResultType).ElemType, flags));

    public StorePtrInst CreatePtrStore(Value addr, Value value, TypeDesc? elemType = null, PointerFlags flags = default)
        => Add(new StorePtrInst(addr, value, elemType ?? ((PointerType)addr.ResultType).ElemType, flags));

    /// <summary> Creates the sequence <c>addr + (nuint)elemOffset * sizeof(elemType)</c>. </summary>
    public Value CreatePtrOffset(Value addr, Value elemOffset, TypeDesc? elemType = null, bool signed = true)
    {
        if (elemOffset is ConstInt { Value: 0 or 1 } c) {
            return c.Value == 0 ? addr : CreatePtrIncrement(addr, elemType);
        }
        if (elemOffset.ResultType.StackType != StackType.NInt) {
            elemOffset = CreateConvert(elemOffset, signed ? PrimType.IntPtr : PrimType.UIntPtr);
        }
        elemType ??= ((PointerType)addr.ResultType).ElemType;
        int size = elemType.Kind.Size();
        var stride = size != 0 ? ConstInt.CreateI(size) : CreateIntrinsic(CilIntrinsic.SizeOf, elemType) as Value;

        return CreateAdd(addr, CreateMul(elemOffset, stride));
    }
    /// <summary> Creates the sequence <c>addr + sizeof(elemType)</c>, i.e. offset to the next element. </summary>
    public Value CreatePtrIncrement(Value addr, TypeDesc? elemType = null)
    {
        elemType ??= ((PointerType)addr.ResultType).ElemType;
        var stride = CreateIntrinsic(CilIntrinsic.SizeOf, elemType) as Value;

        return CreateAdd(addr, stride);
    }


    public IntrinsicInst CreateNewArray(TypeDesc elemType, Value length)
        => Add(new IntrinsicInst(CilIntrinsic.NewArray, elemType, length));


    public void CreateMarker(string text)
        => Add(new IntrinsicInst(IRIntrinsic.Marker, ConstString.Create(text)));

    /// <summary> Creates the <see langword="default"/> value for the given type. </summary>
    public Value CreateDefaultOf(TypeDesc type)
    {
        if (type.Kind == TypeKind.Struct) {
            var slot = new Variable(type, exposed: true);
            CreateIntrinsic(CilIntrinsic.InitObj, type, CreateVarAddr(slot));
            return CreateVarLoad(slot);
        }
        return Const.CreateZero(type);
    }

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
            AppendInitial(inst);
        }
        _last = inst;
    }

    private void AppendInitial(Instruction inst)
    {
        switch (_initialDir) {
            case InsertionDir.Before:   _block.InsertFirst(inst); break;
            case InsertionDir.After:    _block.InsertLast(inst); break;
            default: throw new UnreachableException();
        }
        _initialDir = InsertionDir._Invalid;
    }
}
public enum InsertionDir
{
    /// <summary> Sentinel. For internal use only. </summary>
    _Invalid,

    /// <summary>
    /// Inserts new instructions before the one specified in SetPosition().
    /// If relative to a block, inserts new instructions at the start, after phi and guard instructions.
    /// </summary>
    Before,

    /// <summary>
    /// Inserts new instructions after the one specified in SetPosition().
    /// If relative to a block, inserts new instructions at the end, after the terminator branch.
    /// </summary>
    After,

    /// <summary> Inserts new instructions before the block terminator branch. </summary>
    BeforeLast
}