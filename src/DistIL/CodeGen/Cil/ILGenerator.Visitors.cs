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
        Push(inst.Address);
        EmitLoadOrStorePtr(inst);
    }
    public void Visit(StorePtrInst inst)
    {
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

    public void Visit(ArrayLenInst inst)
    {
        Push(inst.Array);
        _asm.Emit(ILCode.Ldlen);
    }
    //TODO: array inst prefixes: no. / readonly.
    public void Visit(LoadArrayInst inst)
    {
        Push(inst.Array);
        Push(inst.Index);

        var code = ILTables.GetArrayElemMacro(inst.ElemType, ld: true);
        if (code != default) {
            _asm.Emit(code);
        } else {
            _asm.Emit(ILCode.Ldelem, inst.ElemType);
        }
    }
    public void Visit(StoreArrayInst inst)
    {
        Push(inst.Array);
        Push(inst.Index);
        Push(inst.Value);

        var code = ILTables.GetArrayElemMacro(inst.ElemType, ld: false);
        if (code != default) {
            _asm.Emit(code);
        } else {
            _asm.Emit(ILCode.Stelem, inst.ElemType);
        }
    }
    public void Visit(ArrayAddrInst inst)
    {
        Push(inst.Array);
        Push(inst.Index);
        _asm.Emit(ILCode.Ldelema, inst.ElemType);
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
        if (inst.Is(IRIntrinsicId.Marker)) return;

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
            EmitFallthroughBranch(ILCode.Br, inst.Then);
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
        //FIXME: this is confusing
        var (thenBlock, elseBlock) = invert ? (inst.Then, inst.Else) : (inst.Else, inst.Then);
        EmitFallthroughBranch(code, thenBlock, elseBlock);
    }
    public void Visit(SwitchInst inst)
    {
        Push(inst.TargetIndex);
        EmitFallthroughBranch(ILCode.Switch, inst.DefaultTarget, inst.GetIndexedTargets());
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
        EmitFallthroughBranch(ILCode.Leave, inst.Target);
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