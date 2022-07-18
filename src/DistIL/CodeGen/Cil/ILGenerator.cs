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
    Dictionary<Variable, int> _varSlots = new();
    Dictionary<Instruction, Variable> _instSlots = new();

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
            Locals = _varSlots.Keys.ToList(),
            InitLocals = true //TODO: preserve InitLocals
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

    private Label GetLabel(BasicBlock block)
    {
        return _blockLabels.GetOrAddRef(block) ??= new();
    }
    private Variable GetSlot(Instruction inst)
    {
        return _instSlots.GetOrAddRef(inst) ??= new(inst.ResultType, name: $"expr{_instSlots.Count}");
    }

    private void EmitVarInst(Value var, VarOp op)
    {
        int index;
        if (var is Argument arg) {
            index = arg.Index;
        } else if (!_varSlots.TryGetValue((Variable)var, out index)) {
            _varSlots[(Variable)var] = index = _varSlots.Count;
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
}