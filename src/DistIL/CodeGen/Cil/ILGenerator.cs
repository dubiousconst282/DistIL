namespace DistIL.CodeGen.Cil;

using DistIL.AsmIO;
using DistIL.IR;

public partial class ILGenerator : InstVisitor
{
    ILAssembler _asm = new();

    Dictionary<Instruction, Variable> _temps = new();
    Dictionary<BasicBlock, Label> _blockLabels = new();

    Dictionary<Variable, int> _varTable = new();

    public void EmitMethod(MethodDef method)
    {
        foreach (var block in method.Body!) {
            _asm.MarkLabel(GetLabel(block));
            EmitBlock(block);
        }
        var body = method.ILBody!;
        var bakedAsm = _asm.Bake();
        body.Instructions = bakedAsm.Code.ToList();
        body.Locals = _varTable.Keys.ToList();
        body.MaxStack = bakedAsm.MaxStack;
    }

    private void EmitBlock(BasicBlock block)
    {
        //Generate code by treating instructions as trees:
        //whenever a instruction has only one use, and no side effects between
        //def and use we consider it as a leaf (won't emit it in this loop).
        //Instructions that don't satisfy this are copied into a temp variable,
        //and loaded when needed.
        foreach (var inst in block) {
            if (!inst.HasResult || inst.NumUses == 0) {
                inst.Accept(this);
                if (inst.HasResult) {
                    _asm.Emit(ILCode.Pop);
                }
            } else if (inst.HasResult && NeedsTemp(inst)) {
                var tempVar = new Variable(inst.ResultType, false, $"tmp" + _temps.Count);
                _temps.Add(inst, tempVar);

                inst.Accept(this);
                EmitVarInst(tempVar, VarOp.Store);
            }
            //else: this is a leaf
        }
    }

    private bool NeedsTemp(Instruction def)
    {
        if (def.NumUses >= 2) return true;

        var user = def.GetFirstUser()!;
        //Check if they are in the same block
        if (user.Block != def.Block) return true;
        
        //Check if there are side effects between def and use
        for (var inst = def.Next!; inst != user; inst = inst.Next!) {
            if (inst.HasSideEffects) {
                return true;
            }
        }
        return false;
    }

    /// <summary> Returns whether the instruction can be inlined / used as a subexpression. </summary>
    private bool CanInline(Instruction inst)
    {
        return !_temps.ContainsKey(inst);
    }

    private Label GetLabel(BasicBlock block)
    {
        return _blockLabels.GetOrAddRef(block) ??= new();
    }

    private void EmitVarInst(Variable var, VarOp op)
    {
        int index;
        if (var is Argument arg) {
            index = arg.Index;
        } else if (!_varTable.TryGetValue(var, out index)) {
            _varTable[var] = index = _varTable.Count;
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
            case Argument arg: {
                EmitVarInst(arg, VarOp.Load);
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
                if (_temps.TryGetValue(inst, out var tempVar)) {
                    EmitVarInst(tempVar, VarOp.Load);
                } else {
                    inst.Accept(this);
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
        EmitVarInst(inst.Source, VarOp.Load);
    }
    public void Visit(StoreVarInst inst)
    {
        Push(inst.Value);
        EmitVarInst(inst.Dest, VarOp.Store);
    }
    public void Visit(VarAddrInst inst)
    {
        EmitVarInst(inst.Source, VarOp.Addr);
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
        if (inst.Method is not MethodDef) throw null!;
        Assert(inst.Method is MethodDef);
        var code = inst.IsVirtual ? ILCode.Callvirt : ILCode.Call;
        _asm.Emit(code, inst.Method);
    }
    public void Visit(NewObjInst inst)
    {
        foreach (var arg in inst.Args) {
            Push(arg);
        }
        Assert(inst.Constructor is MethodDef);
        _asm.Emit(ILCode.Newobj, inst.Constructor);
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

        if (cond is CompareInst cmp && CanInline(cmp) &&
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