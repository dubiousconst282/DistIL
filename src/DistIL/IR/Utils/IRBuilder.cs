namespace DistIL.IR.Utils;

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
            // Block is either empty, or dir == After
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

    public void SetBranch(BasicBlock target, bool replace = true) 
    {
        if (replace || _block.Last == null || !_block.Last.IsBranch) {
            _block.SetBranch(target);
        }
    }

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
    /// <summary>
    /// Terminates the current block with a new branch <c>goto cond ? newBlock : elseBlock</c>,
    /// sets the builder position to the start of the new block, and calls <paramref name="emitElse"/> to build the code for `elseBlock`.
    /// A branch to the new block will be placed at the `elseBlock` builder if t does not already has one.
    /// </summary>
    public void Fork(Value cond, Action<IRBuilder, BasicBlock> emitElse)
    {
        var elseBlock = Method.CreateBlock(insertAfter: Block);
        var newBlock = Method.CreateBlock(insertAfter: elseBlock);

        var elseBuilder = new IRBuilder(elseBlock);
        emitElse(elseBuilder, newBlock);
        elseBuilder.SetBranch(newBlock, replace: false);

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
    public Value CreateUlt(Value left, Value right) => CreateCmp(CompareOp.Ult, left, right);
    public Value CreateUgt(Value left, Value right) => CreateCmp(CompareOp.Ugt, left, right);
    public Value CreateUle(Value left, Value right) => CreateCmp(CompareOp.Ule, left, right);
    public Value CreateUge(Value left, Value right) => CreateCmp(CompareOp.Uge, left, right);

    public Value CreateNot(Value val)
    {
        return ConstFolding.FoldUnary(UnaryOp.Not, val) ??
               Emit(new UnaryInst(UnaryOp.Not, val));
    }

    public Value CreateSelect(Value cond, Value ifTrue, Value ifFalse, TypeDesc? resultType = null)
    {
        return ConstFolding.FoldSelect(cond, ifTrue, ifFalse) ??
               Emit(new SelectInst(cond, ifTrue, ifFalse, resultType ?? InferResultType(ifTrue.ResultType, ifFalse.ResultType)));

        static TypeDesc InferResultType(TypeDesc a, TypeDesc b)
        {
            if (a == b) return a;

            // Allow cases like `select cond, {int}, {bool}`  (and for any other smallint)
            Ensure.That(a.StackType == b.StackType && a.StackType == StackType.Int, 
                        "Cannot infer result type for SelectInst with differing operand types");

            // Pick largest type, normalizing to signed if both have the same size.
            int sizeA = a.Kind.Size(), sizeB = b.Kind.Size();
            return sizeA == sizeB ? PrimType.GetFromKind(a.Kind.GetSigned()) : 
                   sizeA > sizeB ? a : b;
        }
    }

    public Value CreateMin(Value x, Value y, bool unsigned = false)
    {
        var type = x.ResultType;
        Ensure.That(type.IsStackAssignableTo(y.ResultType));

        var op = type.IsFloat() ? CompareOp.FOlt : 
                 unsigned ? CompareOp.Ult : CompareOp.Slt;
        return CreateSelect(CreateCmp(op, x, y), x, y);
    }
    public Value CreateMax(Value x, Value y, bool unsigned = false)
    {
        var type = x.ResultType;
        Ensure.That(type.IsStackAssignableTo(y.ResultType));

        var op = type.IsFloat() ? CompareOp.FOgt :
                 unsigned ? CompareOp.Ugt : CompareOp.Sgt;
        return CreateSelect(CreateCmp(op, x, y), x, y);
    }

    public Value CreateConvert(Value srcValue, TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
    {
        return ConstFolding.FoldConvert(srcValue, dstType, checkOverflow, srcUnsigned) ??
               Emit(new ConvertInst(srcValue, dstType, checkOverflow, srcUnsigned));
    }


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


    public Instruction CreateFieldLoad(FieldDesc field, Value? obj = null, bool inBounds = false)
    {
        if (obj != null && obj.ResultType.IsValueType) {
            return Emit(new ExtractFieldInst(field, obj));
        }
        return CreateLoad(CreateFieldAddr(field, obj, inBounds));
    }

    public StoreInst CreateFieldStore(FieldDesc field, Value? obj, Value value, bool inBounds = false)
        => CreateStore(CreateFieldAddr(field, obj, inBounds), value);

    public FieldAddrInst CreateFieldAddr(FieldDesc field, Value? obj = null, bool inBounds = false)
        => Emit(new FieldAddrInst(field, obj, inBounds));


    public Instruction CreateFieldLoad(string fieldName, Value obj)
        => CreateFieldLoad(GetInstanceType(obj).FindField(fieldName), obj);
        
    public StoreInst CreateFieldStore(string fieldName, Value obj, Value value)
        => CreateFieldStore(GetInstanceType(obj).FindField(fieldName), obj, value);

    public IntrinsicInst CreateNewArray(TypeDesc elemType, Value length)
        => Emit(new CilIntrinsic.NewArray(elemType, length));

    public IntrinsicInst CreateArrayLen(Value array)
        => Emit(new CilIntrinsic.ArrayLen(array));

    public LoadInst CreateArrayLoad(Value array, Value index, TypeDesc? elemType = null, bool inBounds = false)
    {
        var addr = CreateArrayAddr(array, index, elemType, inBounds, readOnly: true);
        return CreateLoad(addr);
    }

    public StoreInst CreateArrayStore(Value array, Value index, Value value, TypeDesc? elemType = null, bool inBounds = false)
    {
        var addr = CreateArrayAddr(array, index, elemType, inBounds);
        return CreateStore(addr, value);
    }

    public ArrayAddrInst CreateArrayAddr(Value array, Value index, TypeDesc? elemType = null, bool inBounds = false, bool readOnly = false)
    {
        return Emit(new ArrayAddrInst(array, index, elemType, inBounds, readOnly));
    }

    public LoadInst CreateLoad(Value addr, TypeDesc? elemType = null, PointerFlags flags = default)
        => Emit(new LoadInst(addr, elemType, flags));

    public StoreInst CreateStore(Value addr, Value value, TypeDesc? elemType = null, PointerFlags flags = default)
        => Emit(new StoreInst(addr, value, elemType, flags));

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

    /// <summary> Creates the <see langword="default"/> value for the given type. </summary>
    public Value CreateDefaultOf(TypeDesc type)
    {
        if (type.Kind == TypeKind.Struct) {
            var slot = new LocalSlot(type, "tmpZeroInit");
            Emit(new CilIntrinsic.MemSet(slot, type));
            return CreateLoad(slot);
        }
        return Const.CreateZero(type);
    }

    /// <summary> Creates an instruction that stores the <see langword="default"/> value into the given address. </summary>
    public Instruction CreateInitObj(Value addr, TypeDesc? objType = null)
    {
        objType ??= ((PointerType)addr.ResultType).ElemType;
        
        return objType.Kind == TypeKind.Struct 
            ? Emit(new CilIntrinsic.MemSet(addr, objType))
            : CreateStore(addr, Const.CreateZero(objType));
    }

    public IntrinsicInst CreateBox(TypeDesc valueType, Value val)
        => Emit(new CilIntrinsic.Box(valueType, val));
    public IntrinsicInst CreateUnboxObj(TypeDesc valueType, Value val)
        => Emit(new CilIntrinsic.UnboxObj(valueType, val));
    public IntrinsicInst CreateUnboxRef(TypeDesc valueType, Value val)
        => Emit(new CilIntrinsic.UnboxRef(valueType, val));

    public IntrinsicInst CreateCastClass(TypeDesc destType, Value obj)
        => Emit(new CilIntrinsic.CastClass(destType, obj));
    public IntrinsicInst CreateAsInstance(TypeDesc destType, Value obj)
        => Emit(new CilIntrinsic.AsInstance(destType, obj));

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