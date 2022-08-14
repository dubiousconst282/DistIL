namespace DistIL.CodeGen.Cil;

using DistIL.AsmIO;
using DistIL.IR;

partial class ILGenerator
{
    public void VisitDefault(Instruction inst)
    {
        throw new NotImplementedException("Missing emitter for " + inst.GetType().Name);
    }

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

        var addTypeDesc = inst.Address.ResultType;
        var interpType = inst.ElemType;

        var refCode = isLoad ? ILCode.Ldind_Ref : ILCode.Stind_Ref;
        var objCode = isLoad ? ILCode.Ldobj : ILCode.Stobj;

        if (!interpType.IsValueType && addTypeDesc.ElemType == interpType) {
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
        switch (inst.Id) {
            case IntrinsicId.Marker: break;
            case IntrinsicId.NewArray: {
                Push(inst.Args[0]);
                _asm.Emit(ILCode.Newarr, (inst.ResultType as ArrayType)!.ElemType);
                break;
            }
            case IntrinsicId.LoadToken: {
                _asm.Emit(ILCode.Ldtoken, inst.Args[0]);
                break;
            }
            case IntrinsicId.IsInstance: {
                Push(inst.Args[0]);
                _asm.Emit(ILCode.Isinst, inst.Args[1]);
                break;
            }
            case IntrinsicId.CastClass: {
                Push(inst.Args[0]);
                _asm.Emit(ILCode.Castclass, inst.ResultType);
                break;
            }
            default: throw new NotSupportedException($"Intrinsic {inst.Id}");
        }
    }

    public void Visit(BranchInst inst)
    {
        if (inst.IsConditional) {
            //`br cmp.op(x, y), @then;`  ->  `br.op x, y, @then;`
            if (inst.Cond is CompareInst cmp && _forest.IsLeaf(cmp) &&
                ILTables.GetBranchCode(cmp.Op) is var code && code != default
            ) {
                Push(cmp.Left);

                //`x eq/ne [0|null]`  ->  `brfalse/brtrue`
                if (cmp is { Op: CompareOp.Eq or CompareOp.Ne, Right: ConstInt { Value: 0 } or ConstNull }) {
                    code = cmp.Op == CompareOp.Eq ? ILCode.Brfalse : ILCode.Brtrue;
                } else {
                    Push(cmp.Right);
                }
            } else {
                Push(inst.Cond);
                code = ILCode.Brtrue;
            }
            _asm.Emit(code, inst.Then);
        }
        EmitFallthrough(inst.IsJump ? inst.Then : inst.Else);
    }
    public void Visit(SwitchInst inst)
    {
        Push(inst.Value);
        _asm.Emit(ILCode.Switch, inst.GetTargets().Skip(1).ToArray());
        EmitFallthrough(inst.DefaultTarget);
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
        _asm.Emit(ILCode.Nop);
    }
    public void Visit(LeaveInst inst)
    {
        _asm.Emit(ILCode.Leave, inst.Target);
    }
    public void Visit(ContinueInst inst)
    {
        if (inst.IsFromFilter) {
            Push(inst.FilterResult);
            _asm.Emit(ILCode.Endfilter);
        } else {
            _asm.Emit(ILCode.Endfinally);
        }
    }
}