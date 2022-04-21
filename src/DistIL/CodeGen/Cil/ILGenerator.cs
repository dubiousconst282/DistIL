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
        foreach (var block in method) {
            _asm.MarkLabel(GetLabel(block));
            EmitBlock(block);
        }
        var body = method.Body!;
        body.Instructions.Clear();
        body.Instructions.AddRange(_asm.Bake());
        body.Locals.Clear();
        body.Locals.AddRange(_varTable.Keys);

        System.IO.File.WriteAllText("../../logs/codegen.txt", _asm.ToString());
    }

    private void EmitBlock(BasicBlock block)
    {
        //Generate code by treating instructions as trees:
        //whenever a instruction has only one use, and no side effects between
        //def and use we consider it as a leaf (won't emit it in this loop).
        //Instructions that don't satisfy this are copied into a temp variable,
        //and loaded when needed.
        foreach (var inst in block) {
            if (!inst.HasResult || inst.Uses.Count == 0) {
                inst.Accept(this);
                if (inst.HasResult) {
                    _asm.Emit(ILCode.Pop);
                }
            }
            else if (inst.HasResult && NeedsTemp(inst)) {
                var tempVar = new Variable(inst.ResultType, false, name: $"tmp" + _temps.Count);
                _temps.Add(inst, tempVar);

                inst.Accept(this);
                _asm.Emit(ILCode.Stloc, GetVarIndex(tempVar));
            }
            //else: this is a leaf
        }
    }

    private bool NeedsTemp(Instruction def)
    {
        if (def.Uses.Count >= 2) return true;

        var use = def.GetUse(0);
        //Check if they are in the same block
        if (use.Block != def.Block) return true;
        
        //Check if there are side effects between def and use
        for (var inst = def.Next!; inst != use; inst = inst.Next!) {
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

    private int GetVarIndex(Variable var)
    {
        ref int index = ref _varTable.GetOrAddRef(var, out bool exists);
        if (!exists) {
            index = _varTable.Count - 1;
        }
        return index;
    }

    private void Push(Value value)
    {
        switch (value) {
            case Argument arg: {
                _asm.Emit(ILCode.Ldarg, arg.Index);
                break;
            }
            case ConstInt cons: {
                if (cons.IsInt) {
                    _asm.Emit(ILCode.Ldc_I4, (int)cons.Value);
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
                    _asm.Emit(ILCode.Ldloc, GetVarIndex(tempVar));
                } else {
                    inst.Accept(this);
                }
                break;
            }
            default: throw null!;
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
        if (inst.Source is Argument arg) {
            _asm.Emit(ILCode.Ldarg, arg.Index);
        } else {
            _asm.Emit(ILCode.Ldloc, GetVarIndex(inst.Source));
        }
    }
    public void Visit(StoreVarInst inst)
    {
        Push(inst.Value);
        if (inst.Dest is Argument arg) {
            _asm.Emit(ILCode.Starg, arg.Index);
        } else {
            _asm.Emit(ILCode.Stloc, GetVarIndex(inst.Dest));
        }
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
        var codes = GetCodeForPtrAcc(interpType);

        var refCode = isLoad ? ILCode.Ldind_Ref : ILCode.Stind_Ref;
        var objCode = isLoad ? ILCode.Ldobj : ILCode.Stobj;
        var code = isLoad ? codes.Ld : codes.St;

        if (!interpType.IsValueType && addrType.ElemType == interpType) {
            _asm.Emit(refCode);
        } else {
            _asm.Emit(code, code == objCode ? interpType : null);
        }
    }

    public void Visit(LoadFieldInst inst)
    {
        EmitLoadOrStoreField(inst, ILCode.Ldfld, ILCode.Ldsfld);
    }
    public void Visit(StoreFieldInst inst)
    {
        EmitLoadOrStoreField(inst, ILCode.Stfld, ILCode.Stsfld);
    }
    private void EmitLoadOrStoreField(FieldAccessInst inst, ILCode instanceCode, ILCode staticCode)
    {
        if (!inst.IsStatic) {
            Push(inst.Obj);
        }
        if (inst is StoreFieldInst store) {
            Push(store.Value);
        }
        var code = inst.IsStatic ? staticCode : instanceCode;
        _asm.Emit(code, inst.Field);
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