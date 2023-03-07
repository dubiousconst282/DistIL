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

    public void Visit(LoadPtrInst inst)
    {
        switch (inst.Address) {
            case ArrayAddrInst addr when addr.ElemType == inst.ElemType && _forest.IsLeaf(addr): {
                EmitLoadOrStoreArray(addr, null);
                return;
            }
        }
        Push(inst.Address);
        EmitLoadOrStorePtr(inst);
    }
    public void Visit(StorePtrInst inst)
    {
        switch (inst.Address) {
            case ArrayAddrInst addr when addr.ElemType == inst.ElemType && _forest.IsLeaf(addr): {
                EmitLoadOrStoreArray(addr, inst.Value);
                return;
            }
        }
        Push(inst.Address);
        Push(inst.Value);
        EmitLoadOrStorePtr(inst);
    }
    private void EmitLoadOrStorePtr(PtrAccessInst inst)
    {
        bool isLoad = inst is LoadPtrInst;
        if (inst.Volatile) _asm.Emit(ILCode.Volatile_);
        if (inst.Unaligned) _asm.Emit(ILCode.Unaligned_, 1); //TODO: keep alignment in IR

        var addrType = inst.Address.ResultType;
        var interpType = inst.ElemType;

        var refCode = isLoad ? ILCode.Ldind_Ref : ILCode.Stind_Ref;
        var objCode = isLoad ? ILCode.Ldobj : ILCode.Stobj;

        if (!interpType.IsValueType && interpType is not GenericParamType && addrType.ElemType == interpType) {
            _asm.Emit(refCode);
        } else {
            var code = ILTables.GetPtrAccessCode(interpType, isLoad);
            _asm.Emit(code, code == objCode ? interpType : null);
        }
    }
    private void EmitLoadOrStoreArray(ArrayAddrInst addr, Value? valToStore)
    {
        bool ld = valToStore == null;

        Push(addr.Array);
        Push(addr.Index);
        if (!ld) Push(valToStore!);

        var code = ILTables.GetArrayElemMacro(addr.ElemType, ld);

        if (code != default) {
            _asm.Emit(code);
        } else {
            _asm.Emit(ld ? ILCode.Ldelem : ILCode.Stelem, addr.ElemType);
        }
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
    public void Visit(PtrOffsetInst inst)
    {
        //Emit (add addr, (mul (conv.i index), sizeof elemType)
        Push(inst.BasePtr);
        Push(inst.Index);

        if (inst.Index.ResultType.StackType != StackType.NInt) {
            _asm.Emit(ILCode.Conv_I);
        }

        if (inst.Stride != 0) {
            _asm.EmitLdcI4(inst.Stride);
        } else {
            _asm.Emit(ILCode.Sizeof, inst.ElemType);
        }
        _asm.Emit(ILCode.Mul);
        _asm.Emit(ILCode.Add);
    }

    public void Visit(LoadFieldInst inst)
    {
        if (!inst.IsStatic) {
            Push(inst.Obj);
        }
        var code = inst.IsStatic ? ILCode.Ldsfld : ILCode.Ldfld;
        _asm.Emit(code, inst.Field);
    }
    public void Visit(StoreFieldInst inst)
    {
        if (!inst.IsStatic) {
            Push(inst.Obj);
        }
        Push(inst.Value);
        var code = inst.IsStatic ? ILCode.Stsfld : ILCode.Stfld;
        _asm.Emit(code, inst.Field);
    }
    public void Visit(FieldAddrInst inst)
    {
        if (!inst.IsStatic) {
            Push(inst.Obj);
        }
        var code = inst.IsStatic ? ILCode.Ldsflda : ILCode.Ldflda;
        _asm.Emit(code, inst.Field);
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

    public void Visit(BranchInst inst)
    {
        if (inst.IsJump) {
            EmitFallthrough(ILCode.Br, inst.Then);
            return;
        }
        //Invert condition if we can fallthrough the true branch
        bool invert = _nextBlock == inst.Then; 
        var code = ILCode.Nop;

        //`br cmp.op(x, y), @then;`  ->  `br.op x, y, @then;`
        if (inst.Cond is CompareInst cmp && _forest.IsLeaf(cmp)) {
            var op = invert ? cmp.Op.GetNegated() : cmp.Op;

            //`x eq|ne [0|null]`  ->  `brfalse/brtrue`
            if (op is CompareOp.Eq or CompareOp.Ne && cmp.Right is ConstInt { Value: 0 } or ConstNull) {
                code = (op == CompareOp.Eq) ? ILCode.Brfalse : ILCode.Brtrue;
                Push(cmp.Left);
            } else {
                code = ILTables.GetBranchCode(op);

                if (code != ILCode.Nop) {
                    Push(cmp.Left);
                    Push(cmp.Right);
                }
            }
        }
        if (code == ILCode.Nop) {
            Push(inst.Cond);
            code = invert ? ILCode.Brfalse : ILCode.Brtrue;
        }
        var (thenBlock, elseBlock) = invert ? (inst.Else, inst.Then) : (inst.Then, inst.Else);
        EmitBranchAndFallthrough(code, thenBlock, elseBlock);
    }
    public void Visit(SwitchInst inst)
    {
        Push(inst.TargetIndex);
        EmitBranchAndFallthrough(ILCode.Switch, inst.GetIndexedTargets(), inst.DefaultTarget);
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
        //Note that copying of phi arguments is done before the block terminator is emitted
        throw new UnreachableException();
    }
}