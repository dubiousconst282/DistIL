namespace DistIL.CodeGen.Cil;

using DistIL.AsmIO;
using DistIL.IR;

public class ILGenerator : InstVisitor
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
        throw new NotImplementedException();
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

#pragma warning disable format

    private static ILCode GetCodeForBinOp(BinaryOp op)
    {
        return op switch {
            BinaryOp.Add  or BinaryOp.FAdd  => ILCode.Add,
            BinaryOp.Sub  or BinaryOp.FSub  => ILCode.Sub,
            BinaryOp.Mul  or BinaryOp.FMul  => ILCode.Mul,
            BinaryOp.SDiv or BinaryOp.FDiv  => ILCode.Div,
            BinaryOp.SRem or BinaryOp.FRem  => ILCode.Rem,
            BinaryOp.UDiv                   => ILCode.Div_Un,
            BinaryOp.URem                   => ILCode.Rem_Un,
            BinaryOp.And                    => ILCode.And,
            BinaryOp.Or                     => ILCode.Or,
            BinaryOp.Xor                    => ILCode.Xor,
            BinaryOp.Shl                    => ILCode.Shl,
            BinaryOp.Shrl                   => ILCode.Shr,
            BinaryOp.Shra                   => ILCode.Shr_Un,
            BinaryOp.AddOvf                 => ILCode.Add_Ovf,
            BinaryOp.SubOvf                 => ILCode.Sub_Ovf,
            BinaryOp.MulOvf                 => ILCode.Mul_Ovf,
            BinaryOp.UAddOvf                => ILCode.Add_Ovf_Un,
            BinaryOp.USubOvf                => ILCode.Sub_Ovf_Un,
            BinaryOp.UMulOvf                => ILCode.Mul_Ovf_Un,
            _ => throw new InvalidOperationException("Invalid binary op")
        };
    }

    private ILCode GetCodeForConv(ConvertInst inst)
    {
        var srcType = inst.Value.ResultType;
        var dstType = inst.ResultType;

        return (dstType.Kind, inst.SrcUnsigned ? 1 : 0, inst.CheckOverflow ? 1 : 0) switch {
            (TypeKind.SByte,   0, 0) => ILCode.Conv_I1,
            (TypeKind.Int16,   0, 0) => ILCode.Conv_I2,
            (TypeKind.Int32,   0, 0) => ILCode.Conv_I4,
            (TypeKind.Int64,   0, 0) => ILCode.Conv_I8,
            (TypeKind.Byte,    0, 0) => ILCode.Conv_U1,
            (TypeKind.UInt16,  0, 0) => ILCode.Conv_U2,
            (TypeKind.UInt32,  0, 0) => ILCode.Conv_U4,
            (TypeKind.UInt64,  0, 0) => ILCode.Conv_U8,
            (TypeKind.IntPtr,  0, 0) => ILCode.Conv_I,
            (TypeKind.UIntPtr, 0, 0) => ILCode.Conv_U,
            (TypeKind.Single,  0, 0) => ILCode.Conv_R4,
            (TypeKind.Double,  0, 0) => ILCode.Conv_R8,
            
            (TypeKind.SByte,   0, 1) => ILCode.Conv_Ovf_I1,
            (TypeKind.Int16,   0, 1) => ILCode.Conv_Ovf_I2,
            (TypeKind.Int32,   0, 1) => ILCode.Conv_Ovf_I4,
            (TypeKind.Int64,   0, 1) => ILCode.Conv_Ovf_I8,
            (TypeKind.Byte,    0, 1) => ILCode.Conv_Ovf_U1,
            (TypeKind.UInt16,  0, 1) => ILCode.Conv_Ovf_U2,
            (TypeKind.UInt32,  0, 1) => ILCode.Conv_Ovf_U4,
            (TypeKind.UInt64,  0, 1) => ILCode.Conv_Ovf_U8,
            (TypeKind.IntPtr,  0, 1) => ILCode.Conv_Ovf_I,
            (TypeKind.UIntPtr, 0, 1) => ILCode.Conv_Ovf_U,
            
            (TypeKind.SByte,   1, 1) => ILCode.Conv_Ovf_I1_Un,
            (TypeKind.Int16,   1, 1) => ILCode.Conv_Ovf_I2_Un,
            (TypeKind.Int32,   1, 1) => ILCode.Conv_Ovf_I4_Un,
            (TypeKind.Int64,   1, 1) => ILCode.Conv_Ovf_I8_Un,
            (TypeKind.Byte,    1, 1) => ILCode.Conv_Ovf_U1_Un,
            (TypeKind.UInt16,  1, 1) => ILCode.Conv_Ovf_U2_Un,
            (TypeKind.UInt32,  1, 1) => ILCode.Conv_Ovf_U4_Un,
            (TypeKind.UInt64,  1, 1) => ILCode.Conv_Ovf_U8_Un,
            (TypeKind.IntPtr,  1, 1) => ILCode.Conv_Ovf_I_Un,
            (TypeKind.UIntPtr, 1, 1) => ILCode.Conv_Ovf_U_Un,

            (TypeKind.Single,  1, 0) => ILCode.Conv_R_Un,
            (TypeKind.Double,  1, 0) => ILCode.Conv_R_Un,

            _ => throw new NotSupportedException("Invalid combination in ConvertInst: " + inst)
        };
    }

    private static bool GetCodeForBranch(CompareOp op, out ILCode code, out bool invert)
    {
        const bool T = true, F = false;
        (code, invert) = op switch {
            CompareOp.Eq  or CompareOp.FOeq => (ILCode.Beq, F),
            CompareOp.Slt or CompareOp.FOlt => (ILCode.Blt, F),
            CompareOp.Sgt or CompareOp.FOgt => (ILCode.Bgt, F),
            CompareOp.Sle or CompareOp.FOle => (ILCode.Ble, F),
            CompareOp.Sge or CompareOp.FOge => (ILCode.Bge, F),
            CompareOp.Ult or CompareOp.FUlt => (ILCode.Blt_Un, F),
            CompareOp.Ugt or CompareOp.FUgt => (ILCode.Bgt_Un, F),
            CompareOp.Ule or CompareOp.FUle => (ILCode.Ble_Un, F),
            CompareOp.Uge or CompareOp.FUge => (ILCode.Bge_Un, F),
            _ => (ILCode.Nop, F)
        };
        return code != ILCode.Nop;
    }
#pragma warning restore format
}