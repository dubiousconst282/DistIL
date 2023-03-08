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
               Emit(new BinaryInst(op, left, right));
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
               Emit(new CompareInst(op, left, right));
    }
    public Value CreateEq(Value left, Value right) => CreateCmp(CompareOp.Eq, left, right);
    public Value CreateNe(Value left, Value right) => CreateCmp(CompareOp.Ne, left, right);
    public Value CreateSlt(Value left, Value right) => CreateCmp(CompareOp.Slt, left, right);
    public Value CreateSgt(Value left, Value right) => CreateCmp(CompareOp.Sgt, left, right);
    public Value CreateSle(Value left, Value right) => CreateCmp(CompareOp.Sle, left, right);
    public Value CreateSge(Value left, Value right) => CreateCmp(CompareOp.Sge, left, right);

    public ConvertInst CreateConvert(Value srcValue, TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
        => Emit(new ConvertInst(srcValue, dstType, checkOverflow, srcUnsigned));


    public CallInst CreateCall(MethodDesc method, params Value[] args)
        => Emit(new CallInst(method, args));

    public CallInst CreateCallVirt(MethodDesc method, params Value[] args)
        => Emit(new CallInst(method, args, true));

    /// <summary> Searches for <paramref name="methodName"/> in the instance object type (first argument), and creates a callvirt instruction for it. </summary>
    public CallInst CreateCallVirt(string methodName, params Value[] args)
    {
        var instanceType = GetInstanceType(args[0]);
        var method = instanceType.FindMethod(methodName, searchBaseAndItfs: true);
        return CreateCallVirt(method, args);
    }


    public NewObjInst CreateNewObj(MethodDesc ctor, params Value[] args)
        => Emit(new NewObjInst(ctor, args));

    public IntrinsicInst CreateIntrinsic(IntrinsicDesc intrinsic, params Value[] args)
        => Emit(new IntrinsicInst(intrinsic, args));


    public LoadPtrInst CreateFieldLoad(FieldDesc field, Value? obj = null, bool inBounds = false)
    {
        var addr = CreateFieldAddr(field, obj, inBounds);
        return CreatePtrLoad(addr);
    }

    public StorePtrInst CreateFieldStore(FieldDesc field, Value? obj, Value value, bool inBounds = false)
    {
        var addr = CreateFieldAddr(field, obj, inBounds);
        return CreatePtrStore(addr, value);
    }

    public FieldAddrInst CreateFieldAddr(FieldDesc field, Value? obj = null, bool inBounds = false)
        => Emit(new FieldAddrInst(field, obj, inBounds));


    public LoadPtrInst CreateFieldLoad(string fieldName, Value obj)
        => CreateFieldLoad(GetInstanceType(obj).FindField(fieldName), obj);


    public LoadVarInst CreateVarLoad(Variable var)
        => Emit(new LoadVarInst(var));

    public StoreVarInst CreateVarStore(Variable var, Value value)
        => Emit(new StoreVarInst(var, value));

    public VarAddrInst CreateVarAddr(Variable var)
        => Emit(new VarAddrInst(var));


    public IntrinsicInst CreateNewArray(TypeDesc elemType, Value length)
        => Emit(new IntrinsicInst(CilIntrinsic.NewArray, elemType, length));

    public IntrinsicInst CreateArrayLen(Value array)
        => Emit(new IntrinsicInst(CilIntrinsic.ArrayLen, array));

    public LoadPtrInst CreateArrayLoad(Value array, Value index, TypeDesc? elemType = null, bool inBounds = false)
    {
        var addr = CreateArrayAddr(array, index, elemType, inBounds, readOnly: true);
        return CreatePtrLoad(addr);
    }

    public StorePtrInst CreateArrayStore(Value array, Value index, Value value, TypeDesc? elemType = null, bool inBounds = false)
    {
        var addr = CreateArrayAddr(array, index, elemType, inBounds);
        return CreatePtrStore(addr, value);
    }

    public ArrayAddrInst CreateArrayAddr(Value array, Value index, TypeDesc? elemType = null, bool inBounds = false, bool readOnly = false)
    {
        return Emit(new ArrayAddrInst(array, index, elemType, inBounds, readOnly));
    }

    public LoadPtrInst CreatePtrLoad(Value addr, TypeDesc? elemType = null, PointerFlags flags = default)
        => Emit(new LoadPtrInst(addr, elemType, flags));

    public StorePtrInst CreatePtrStore(Value addr, Value value, TypeDesc? elemType = null, PointerFlags flags = default)
        => Emit(new StorePtrInst(addr, value, elemType, flags));

    /// <summary> Creates the sequence <c>addr + (nint)elemOffset * sizeof(elemType)</c>. </summary>
    public Value CreatePtrOffset(Value addr, Value elemOffset, TypeDesc? elemType = null)
    {
        if (elemOffset is ConstInt { Value: 0 }) {
            return addr;
        }
        elemType ??= ((PointerType)addr.ResultType).ElemType;
        return Emit(new PtrOffsetInst(addr, elemOffset, elemType));
    }
    /// <summary> Creates the sequence <c>addr + sizeof(elemType)</c>, i.e. offset to the next element. </summary>
    public Value CreatePtrIncrement(Value addr, TypeDesc? elemType = null)
    {
        return CreatePtrOffset(addr, ConstInt.CreateI(1), elemType);
    }


    public void CreateMarker(string text)
        => Emit(new IntrinsicInst(IRIntrinsic.Marker, ConstString.Create(text)));

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
    public TInst Emit<TInst>(TInst inst) where TInst : Instruction
    {
        Append(inst);
        return inst;
    }

    private TypeDesc GetInstanceType(Value obj)
    {
        var type = obj.ResultType;

        if (type is ByrefType refType) {
            type = refType.ElemType;
            Ensure.That(type.IsValueType);
        }
        if (type is PrimType prim) {
            type = prim.GetDefinition(Resolver);
        }
        return type;
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