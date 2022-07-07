namespace DistIL.CodeGen.Cil;

using DistIL.AsmIO;
using DistIL.IR;
using DistIL.IR.Utils;

public partial class ILGenerator : InstVisitor
{
    MethodBody _method;
    Forestifier _forest;
    ILAssembler _asm = new();
    Dictionary<BasicBlock, Label> _blockLabels = new();
    Dictionary<Variable, int> _varTable = new();
    Dictionary<Instruction, Variable> _slots = new();

    public ILGenerator(MethodBody method)
    {
        _method = method;
        _forest = new Forestifier(method);
    }

    public ILMethodBody Bake()
    {
        foreach (var block in _method) {
            _asm.MarkLabel(GetLabel(block));
            EmitBlock(block);
        }
        var bakedAsm = _asm.Bake();
        return new ILMethodBody() {
            ExceptionRegions = new(),
            MaxStack = bakedAsm.MaxStack,
            Instructions = bakedAsm.Code.ToList(),
            Locals = _varTable.Keys.ToList()
        };
    }

    private void EmitBlock(BasicBlock block)
    {
        //Emit code for the rooted trees in the forest
        foreach (var inst in block) {
            if (!_forest.IsRootedTree(inst)) continue;

            inst.Accept(this);

            if (inst.NumUses > 0) {
                EmitVarInst(GetSlot(inst), VarOp.Store);
            } else if (inst.HasResult) {
                _asm.Emit(ILCode.Pop); //unused result
            }
        }
    }

    private Label GetLabel(BasicBlock block)
    {
        return _blockLabels.GetOrAddRef(block) ??= new();
    }
    private Variable GetSlot(Instruction inst)
    {
        return _slots.GetOrAddRef(inst) ??= new(inst.ResultType, name: $"expr{_slots.Count}");
    }

    private void EmitVarInst(Value var, VarOp op)
    {
        int index;
        if (var is Argument arg) {
            index = arg.Index;
        } else if (!_varTable.TryGetValue((Variable)var, out index)) {
            _varTable[(Variable)var] = index = _varTable.Count;
        }
        var codes = GetCodesForVar(op, var is Argument);

        if (index < 4 && codes.Inline != ILCode.Nop) {
            _asm.Emit((ILCode)((int)codes.Inline + index));
        } else if (index < 256) {
            _asm.Emit(codes.Short, index);
        } else {
            _asm.Emit(codes.Norm, index);
        }
    }

    private void EmitLdcI4(int value)
    {
        if (value >= -1 && value <= 8) {
            _asm.Emit((ILCode)((int)ILCode.Ldc_I4_0 + value));
        } else if ((sbyte)value == value) {
            _asm.Emit(ILCode.Ldc_I4_S, value);
        } else {
            _asm.Emit(ILCode.Ldc_I4, value);
        }
    }

    private void Push(Value value)
    {
        switch (value) {
            case Variable or Argument: {
                EmitVarInst(value, VarOp.Load);
                break;
            }
            case ConstInt cons: {
                //Emit int, or small long followed by conv.i8
                if (cons.IsInt || (cons.Value == (int)cons.Value)) {
                    EmitLdcI4((int)cons.Value);
                    if (!cons.IsInt) _asm.Emit(ILCode.Conv_I8);
                } else {
                    _asm.Emit(ILCode.Ldc_I8, cons.Value);
                }
                break;
            }
            case ConstFloat cons: {
                if (cons.IsSingle) {
                    _asm.Emit(ILCode.Ldc_R4, (float)cons.Value);
                } else {
                    _asm.Emit(ILCode.Ldc_R8, cons.Value);
                }
                break;
            }
            case ConstString cons: {
                _asm.Emit(ILCode.Ldstr, cons.Value);
                break;
            }
            case ConstNull: {
                _asm.Emit(ILCode.Ldnull);
                break;
            }
            case Instruction inst: {
                if (_forest.IsLeaf(inst)) {
                    inst.Accept(this);
                } else {
                    EmitVarInst(GetSlot(inst), VarOp.Load);
                }
                break;
            }
            default: throw new NotSupportedException(value.GetType().Name + " as operand");
        }
    }

    public void VisitDefault(Instruction inst)
    {
        throw new NotImplementedException("Missing emitter for " + inst.GetType().Name);
    }

    public void Visit(BinaryInst inst)
    {
        Push(inst.Left);
        Push(inst.Right);
        _asm.Emit(GetCodeForBinOp(inst.Op));
    }
    public void Visit(UnaryInst inst)
    {
        Push(inst.Value);
        _asm.Emit(GetCodeForUnOp(inst.Op));
    }
    public void Visit(ConvertInst inst)
    {
        Push(inst.Value);
        _asm.Emit(GetCodeForConv(inst));
    }
    public void Visit(CompareInst inst)
    {
        Push(inst.Left);
        Push(inst.Right);

        var (code, inv) = GetCodeForCompare(inst.Op);
        _asm.Emit(code);
        if (inv) { //!cond
            _asm.Emit(ILCode.Ldc_I4_0);
            _asm.Emit(ILCode.Ceq);
        }
    }

    public void Visit(LoadVarInst inst)
    {
        EmitVarInst(inst.Var, VarOp.Load);
    }
    public void Visit(StoreVarInst inst)
    {
        Push(inst.Value);
        EmitVarInst(inst.Var, VarOp.Store);
    }
    public void Visit(VarAddrInst inst)
    {
        EmitVarInst(inst.Var, VarOp.Addr);
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
        var codes = GetCodeForPtrAcc(interpType);

        var refCode = isLoad ? ILCode.Ldind_Ref : ILCode.Stind_Ref;
        var objCode = isLoad ? ILCode.Ldobj : ILCode.Stobj;
        var code = isLoad ? codes.Ld : codes.St;

        if (!interpType.IsValueType && addTypeDesc.ElemType == interpType) {
            _asm.Emit(refCode);
        } else {
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

        if (_ldelemMacros.TryGetValue(inst.ElemType.Kind, out var code)) {
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

        if (_stelemMacros.TryGetValue(inst.ElemType.Kind, out var code)) {
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
            default: throw new NotSupportedException($"Intrinsic {inst.Id}");
        }
    }

    public void Visit(BranchInst inst)
    {
        var thenLabel = GetLabel(inst.Then);
        if (!inst.IsConditional) {
            _asm.Emit(ILCode.Br, thenLabel);
            return;
        }
        var elseLabel = GetLabel(inst.Else);
        var cond = inst.Cond;

        if (cond is CompareInst cmp && _forest.IsLeaf(cmp) &&
            GetCodeForBranch(cmp.Op, out var brCode, out bool invert))
        {
            Push(cmp.Left);

            //simplify `x == [0|null]` to brfalse, `x != [0|null]` to brtrue
            if (cmp.Op is CompareOp.Eq or CompareOp.Ne && 
                cmp.Right is ConstInt { Value: 0 } or ConstNull)
            {
                brCode = cmp.Op == CompareOp.Eq ? ILCode.Brfalse : ILCode.Brtrue;
            } else {
                Push(cmp.Right);
            }
            _asm.Emit(brCode, invert ? elseLabel : thenLabel);
            _asm.Emit(ILCode.Br, invert ? thenLabel : elseLabel);
        } else {
            //if (cond) goto thenLabel;
            //goto elseLabel
            Push(cond);
            _asm.Emit(ILCode.Brtrue, thenLabel);
            _asm.Emit(ILCode.Br, elseLabel);
        }
    }

    public void Visit(ReturnInst inst)
    {
        if (inst.HasValue) {
            Push(inst.Value);
        }
        _asm.Emit(ILCode.Ret);
    }
}