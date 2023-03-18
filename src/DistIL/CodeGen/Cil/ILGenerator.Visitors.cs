namespace DistIL.CodeGen.Cil;

using DistIL.IR.Intrinsics;

partial class ILGenerator
{
    public void Visit(BinaryInst inst)
    {
        Push(inst.Left);
        Push(inst.Right);
        _asm.Emit(ILTables.GetBinaryCode(inst.Op));
    }
    public void Visit(UnaryInst inst)
    {
        Push(inst.Value);
        _asm.Emit(ILTables.GetUnaryCode(inst.Op));
    }
    public void Visit(ConvertInst inst)
    {
        Push(inst.Value);
        _asm.Emit(ILTables.GetConvertCode(inst));
    }
    public void Visit(CompareInst inst)
    {
        Push(inst.Left);
        Push(inst.Right);

        var (code, inv) = ILTables.GetCompareCode(inst.Op);
        _asm.Emit(code);
        if (inv) { //!cond
            _asm.Emit(ILCode.Ldc_I4_0);
            _asm.Emit(ILCode.Ceq);
        }
    }

    public void Visit(LoadVarInst inst)
    {
        _asm.EmitLoad(inst.Var);
    }
    public void Visit(StoreVarInst inst)
    {
        Push(inst.Value);
        _asm.EmitStore(inst.Var);
    }
    public void Visit(VarAddrInst inst)
    {
        _asm.EmitAddrOf(inst.Var);
    }

    public void Visit(LoadInst inst)
    {
        if (EmitContainedLoadOrStore(inst, null)) return;

        Push(inst.Address);
        EmitLoadOrStorePtr(inst, isLoad: true);
    }
    public void Visit(StoreInst inst)
    {
        if (EmitContainedLoadOrStore(inst, inst.Value)) return;

        Push(inst.Address);
        Push(inst.Value);
        EmitLoadOrStorePtr(inst, isLoad: false);
    }
    private bool EmitContainedLoadOrStore(MemoryInst inst, Value? valToStore)
    {
        if (inst.Address is AddressInst addr && addr.ElemType == inst.ElemType && _forest.IsLeaf(addr)) {
            if (addr is ArrayAddrInst arrayAddr) {
                EmitLoadOrStoreArray(arrayAddr, valToStore);
                return true;
            }
            if (addr is FieldAddrInst fldAddr) {
                EmitLoadOrStoreField(fldAddr, valToStore);
                return true;
            }
        }
        return false;
    }
    private void EmitLoadOrStorePtr(MemoryInst inst, bool isLoad)
    {
        if (inst.IsUnaligned) _asm.Emit(ILCode.Unaligned_, 1); //TODO: keep alignment in IR
        if (inst.IsVolatile) _asm.Emit(ILCode.Volatile_);

        var addrType = inst.Address.ResultType;
        var interpType = inst.ElemType;

        if (interpType.IsValueType || interpType is GenericParamType || addrType.ElemType != interpType) {
            var code = ILTables.GetPtrAccessCode(interpType, isLoad);
            var oper = code is ILCode.Ldobj or ILCode.Stobj ? interpType : null;
            _asm.Emit(code, oper);
        } else {
            _asm.Emit(isLoad ? ILCode.Ldind_Ref : ILCode.Stind_Ref);
        }
    }
    private void EmitLoadOrStoreArray(ArrayAddrInst addr, Value? valToStore)
    {
        bool isLoad = valToStore == null;

        Push(addr.Array);
        Push(addr.Index);
        if (!isLoad) Push(valToStore!);

        var code = ILTables.GetArrayElemCode(addr.ElemType, isLoad);
        var oper = code is ILCode.Ldelem or ILCode.Stelem ? addr.ElemType : null;
        _asm.Emit(code, oper);
    }
    private void EmitLoadOrStoreField(FieldAddrInst addr, Value? valToStore)
    {
        bool isLoad = valToStore == null;

        if (addr.IsInstance) Push(addr.Obj);
        if (!isLoad) Push(valToStore!);

        var code = addr.IsStatic
            ? (isLoad ? ILCode.Ldsfld : ILCode.Stsfld)
            : (isLoad ? ILCode.Ldfld : ILCode.Stfld);
        _asm.Emit(code, addr.Field);
    }

    public void Visit(ArrayAddrInst inst)
    {
        Push(inst.Array);
        Push(inst.Index);

        if (inst.IsReadOnly) {
            _asm.Emit(ILCode.Readonly_);
        }
        _asm.Emit(ILCode.Ldelema, inst.ElemType);
    }
    public void Visit(FieldAddrInst inst)
    {
        if (!inst.IsStatic) {
            Push(inst.Obj);
        }
        var code = inst.IsStatic ? ILCode.Ldsflda : ILCode.Ldflda;
        _asm.Emit(code, inst.Field);
    }
    public void Visit(PtrOffsetInst inst)
    {
        //Emit (add addr, (mul (conv.i index), sizeof elemType)
        Push(inst.BasePtr);
        Push(inst.Index);

        if (inst.Index.ResultType.StackType != StackType.NInt) {
            _asm.Emit(ILCode.Conv_I);
        }

        if (inst.Stride == 0) {
            _asm.Emit(ILCode.Sizeof, inst.ElemType);
            _asm.Emit(ILCode.Mul);
        } else if (inst.Stride > 1) {
            _asm.EmitLdcI4(inst.Stride);
            _asm.Emit(ILCode.Mul);
        }
        _asm.Emit(ILCode.Add);
    }

    public void Visit(CallInst inst)
    {
        foreach (var arg in inst.Args) {
            Push(arg);
        }
        if (inst.Constraint != null) {
            _asm.Emit(ILCode.Constrained_, inst.Constraint);
        }
        var code = inst.IsVirtual ? ILCode.Callvirt : ILCode.Call;
        _asm.Emit(code, inst.Method);
    }
    public void Visit(FuncAddrInst inst)
    {
        if (inst.IsVirtual) {
            Push(inst.Object);
        }
        var code = inst.IsVirtual ? ILCode.Ldvirtftn : ILCode.Ldftn;
        _asm.Emit(code, inst.Method);
    }
    public void Visit(NewObjInst inst)
    {
        foreach (var arg in inst.Args) {
            Push(arg);
        }
        _asm.Emit(ILCode.Newobj, inst.Constructor);
    }
    public void Visit(IntrinsicInst inst)
    {
        if (inst.Intrinsic == IRIntrinsic.Marker) return;

        var intrinsic = (inst.Intrinsic as CilIntrinsic) ?? 
            throw new InvalidOperationException("Only CilIntrinsic`s can be called during codegen");

        switch (intrinsic.Id) {
            case CilIntrinsicId.NewArray:
            case CilIntrinsicId.CastClass:
            case CilIntrinsicId.AsInstance:
            case CilIntrinsicId.Box:
            case CilIntrinsicId.UnboxObj:
            case CilIntrinsicId.UnboxRef:
            case CilIntrinsicId.LoadHandle:
            case CilIntrinsicId.InitObj:
            case CilIntrinsicId.SizeOf: {
                if (inst.Args.Length >= 2) {
                    Push(inst.Args[1]);
                }
                _asm.Emit(intrinsic.Opcode, inst.Args[0]);
                break;
            }
            case CilIntrinsicId.ArrayLen:
            case CilIntrinsicId.Alloca: {
                Push(inst.Args[0]);
                _asm.Emit(intrinsic.Opcode);
                break;
            }
            default: throw new NotSupportedException($"Intrinsic {intrinsic}");
        }
    }
    public void Visit(SelectInst inst)
    {
        var resultReg = _regAlloc.GetRegister(inst);
        EmitCondSelect(inst, resultReg);
        _asm.EmitLoad(resultReg);
    }

    public void Visit(BranchInst inst)
    {
        if (inst.IsJump) {
            EmitFallthrough(ILCode.Br, inst.Then);
            return;
        }
        //Negate condition if we can fallthrough the true branch
        bool negate = _nextBlock == inst.Then;
        var (thenBlock, elseBlock) = negate ? (inst.Else, inst.Then) : (inst.Then, inst.Else);
        var brCode = GetBranchCodeAndPushCond(inst.Cond, negate);
        EmitBranchAndFallthrough(brCode, (ILLabel)thenBlock, elseBlock);
    }

    private ILCode GetBranchCodeAndPushCond(Value cond, bool negate)
    {
        //`br cmp.op(x, y), @then;`  ->  `br.op x, y, @then;`
        if (cond is CompareInst cmp && _forest.IsLeaf(cmp)) {
            var op = negate ? cmp.Op.GetNegated() : cmp.Op;

            //`x eq|ne [0|null]`  ->  `brfalse/brtrue`
            if (op is CompareOp.Eq or CompareOp.Ne && cmp.Right is ConstInt { Value: 0 } or ConstNull) {
                Push(cmp.Left);
                return (op == CompareOp.Eq) ? ILCode.Brfalse : ILCode.Brtrue;
            }
            //Use macro for branch with compare
            if (ILTables.GetBranchCode(op) is var code && code != ILCode.Nop) {
                Push(cmp.Left);
                Push(cmp.Right);
                return code;
            }
        }
        Push(cond);
        return negate ? ILCode.Brfalse : ILCode.Brtrue;
    }

    public void Visit(SwitchInst inst)
    {
        Push(inst.TargetIndex);

        var targets = new ILLabel[inst.NumTargets];
        for (int i = 0; i < targets.Length; i++) {
            targets[i] = inst.GetTarget(i);
        }
        EmitBranchAndFallthrough(ILCode.Switch, targets, inst.DefaultTarget);
    }
    public void Visit(ReturnInst inst)
    {
        if (inst.HasValue) {
            Push(inst.Value);
        }
        _asm.Emit(ILCode.Ret);
    }

    public void Visit(ThrowInst inst)
    {
        if (!inst.IsRethrow) {
            Push(inst.Exception);
            _asm.Emit(ILCode.Throw);
        } else {
            _asm.Emit(ILCode.Rethrow);
        }
    }

    public void Visit(GuardInst inst)
    {
        //Guards are purely metadata and don't do anything.
        //See ILGenerator.Generate() for how they're actually emitted.
    }
    public void Visit(LeaveInst inst)
    {
        EmitFallthrough(ILCode.Leave, inst.Target);
    }
    public void Visit(ResumeInst inst)
    {
        if (inst.IsFromFilter) {
            Push(inst.FilterResult);
            _asm.Emit(ILCode.Endfilter);
        } else {
            _asm.Emit(ILCode.Endfinally);
        }
    }

    public void Visit(PhiInst inst)
    {
        //Copying of phi arguments is done before the block terminator is emitted.
        throw new UnreachableException();
    }
}