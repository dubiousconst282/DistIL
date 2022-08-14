namespace DistIL.Frontend;

using DistIL.AsmIO;
using DistIL.IR;

internal class BlockState
{
    readonly ILImporter _importer;
    readonly MethodBody _body;
    private ModuleDef _mod => _body.Definition.Module;

    public BasicBlock Block { get; }
    private ArrayStack<Value> _stack;

    //Variables that were left in the stack after the execution of a predecessor block.
    private ArrayStack<PhiInst>? _entryStack;
    private List<BlockState> _succStates = new();

    private InstFlags _prefixFlags = InstFlags.None;
    private GuardInst? _activeGuard;
    private TypeDesc? _callConstraint;

    public BlockState(ILImporter importer)
    {
        _importer = importer;
        _body = importer._body;
        Block = _body.CreateBlock();
        _stack = new ArrayStack<Value>(_body.Definition.ILBody!.MaxStack);
    }

    public void Emit(Instruction inst)
    {
        Block.InsertLast(inst);
    }

    public void SetActiveGuard(GuardInst guard)
    {
        _activeGuard ??= guard;
    }

    public void PushNoEmit(Value value)
    {
        _stack.Push(value);
    }
    public void Push(Value value)
    {
        if (value is Instruction inst) {
            Emit(inst);
        }
        _stack.Push(value);
    }
    public Value Pop()
    {
        return _stack.Pop();
    }

    private bool HasPrefix(InstFlags flag)
    {
        return (_prefixFlags & flag) == flag;
    }

    private BasicBlock AddSucc(int offset)
    {
        var succ = _importer.GetBlock(offset);
        Block.Connect(succ.Block);
        _succStates.Add(succ);
        return succ.Block;
    }
    //Adds the last instruction in the block (a branch),
    //and propagate variables left on the stack to successor blocks.
    private void TerminateBlock(Instruction branch)
    {
        PropagateStack();
        Emit(branch);
    }
    private void PropagateStack()
    {
        var exitStack = _stack;
        if (exitStack.Count == 0) return;

        foreach (var succ in _succStates) {
            var entryStack = succ._entryStack;

            if (entryStack == null) {
                //Create dummy phis
                succ._entryStack = entryStack = new ArrayStack<PhiInst>(exitStack.Count);
                foreach (var value in exitStack) {
                    var phi = new PhiInst(new PhiArg(Block, value));
                    entryStack.Push(phi);
                    succ.Push(phi);
                }
            } else {
                //Update phis
                for (int i = 0; i < exitStack.Count; i++) {
                    var phi = entryStack[i];
                    var value = exitStack[i];
                    //Ensure types are compatible (FIXME: III.1.8.1.3)
                    if (value.ResultType.StackType != phi.ResultType.StackType) {
                        throw Error("Inconsistent evaluation stack between basic blocks.");
                    }
                    phi.AddArg(Block, value);
                }
            }
        }
    }

    /// <summary> Translates the IL code into IR instructions. </summary>
    public void ImportCode(Span<ILInstruction> code)
    {
        foreach (ref var inst in code) {
            var prefix = InstFlags.None;
            var opcode = inst.OpCode;

            #pragma warning disable format
            const bool T = true, F = false;

            switch (opcode) {
                #region Load Const
                case >= ILCode.Ldc_I4_M1 and <= ILCode.Ldc_I4_8:
                    ImportConst(ConstInt.CreateI((int)opcode - (int)ILCode.Ldc_I4_0));
                    break;
                case ILCode.Ldc_I4_S:
                case ILCode.Ldc_I4:
                    ImportConst(ConstInt.CreateI((int)inst.Operand!));
                    break;
                case ILCode.Ldc_I8: ImportConst(ConstInt.CreateL((long)inst.Operand!)); break;
                case ILCode.Ldc_R4: ImportConst(ConstFloat.CreateS((float)inst.Operand!)); break;
                case ILCode.Ldc_R8: ImportConst(ConstFloat.CreateD((double)inst.Operand!)); break;
                case ILCode.Ldnull: ImportConst(ConstNull.Create()); break;
                case ILCode.Ldstr:  ImportConst(ConstString.Create((string)inst.Operand!)); break;
                #endregion

                #region Load/Store Local/Argument
                case >= ILCode.Ldloc_0 and <= ILCode.Ldloc_3:
                    ImportVarLoad(opcode - ILCode.Ldloc_0);
                    break;
                case ILCode.Ldloc_S:
                case ILCode.Ldloc:
                    ImportVarLoad((int)inst.Operand!);
                    break;

                case >= ILCode.Stloc_0 and <= ILCode.Stloc_3:
                    ImportVarStore(opcode - ILCode.Stloc_0);
                    break;
                case ILCode.Stloc_S:
                case ILCode.Stloc:
                    ImportVarStore((int)inst.Operand!);
                    break;

                case >= ILCode.Ldarg_0 and <= ILCode.Ldarg_3:
                    ImportVarLoad(opcode - ILCode.Ldarg_0, true);
                    break;
                case ILCode.Ldarg_S:
                case ILCode.Ldarg:
                    ImportVarLoad((int)inst.Operand!, true);
                    break;

                case ILCode.Starg_S:
                case ILCode.Starg:
                    ImportVarStore((int)inst.Operand!, true);
                    break;

                case ILCode.Ldloca_S:
                case ILCode.Ldloca:
                    ImportVarAddr((int)inst.Operand!);
                    break;
                case ILCode.Ldarga_S:
                case ILCode.Ldarga:
                    ImportVarAddr((int)inst.Operand!, true);
                    break;
                #endregion

                #region Arithmetic/Bitwise
                case ILCode.Add:        ImportBinary(BinaryOp.Add, BinaryOp.FAdd); break;
                case ILCode.Sub:        ImportBinary(BinaryOp.Sub, BinaryOp.FSub); break;
                case ILCode.Mul:        ImportBinary(BinaryOp.Mul, BinaryOp.FMul); break;
                case ILCode.Div:        ImportBinary(BinaryOp.SDiv, BinaryOp.FDiv); break;
                case ILCode.Rem:        ImportBinary(BinaryOp.SRem, BinaryOp.FRem); break;
                case ILCode.Div_Un:     ImportBinary(BinaryOp.UDiv); break;
                case ILCode.Rem_Un:     ImportBinary(BinaryOp.URem); break;

                case ILCode.And:        ImportBinary(BinaryOp.And); break;
                case ILCode.Or:         ImportBinary(BinaryOp.Or); break;
                case ILCode.Xor:        ImportBinary(BinaryOp.Xor); break;
                case ILCode.Shl:        ImportBinary(BinaryOp.Shl); break;
                case ILCode.Shr:        ImportBinary(BinaryOp.Shra); break;
                case ILCode.Shr_Un:     ImportBinary(BinaryOp.Shrl); break;

                case ILCode.Add_Ovf:    ImportBinary(BinaryOp.AddOvf); break;
                case ILCode.Add_Ovf_Un: ImportBinary(BinaryOp.UAddOvf); break;
                case ILCode.Sub_Ovf:    ImportBinary(BinaryOp.SubOvf); break;
                case ILCode.Sub_Ovf_Un: ImportBinary(BinaryOp.USubOvf); break;
                case ILCode.Mul_Ovf:    ImportBinary(BinaryOp.MulOvf); break;
                case ILCode.Mul_Ovf_Un: ImportBinary(BinaryOp.UMulOvf); break;

                case ILCode.Neg:
                case ILCode.Not:
                    ImportUnary(opcode);
                    break;
                #endregion

                #region Convert From/To Primitive
                case ILCode.Conv_I1: ImportConv(PrimType.SByte,   F); break;
                case ILCode.Conv_I2: ImportConv(PrimType.Int16,   F); break;
                case ILCode.Conv_I4: ImportConv(PrimType.Int32,   F); break;
                case ILCode.Conv_I8: ImportConv(PrimType.Int64,   F); break;
                case ILCode.Conv_U1: ImportConv(PrimType.Byte,    F); break;
                case ILCode.Conv_U2: ImportConv(PrimType.UInt16,  F); break;
                case ILCode.Conv_U4: ImportConv(PrimType.UInt32,  F); break;
                case ILCode.Conv_U8: ImportConv(PrimType.UInt64,  F); break;
                case ILCode.Conv_R4: ImportConv(PrimType.Single,  F); break;
                case ILCode.Conv_R8: ImportConv(PrimType.Double,  F); break;
                case ILCode.Conv_I:  ImportConv(PrimType.IntPtr,  F); break;
                case ILCode.Conv_U:  ImportConv(PrimType.UIntPtr, F); break;

                case ILCode.Conv_Ovf_I1: ImportConv(PrimType.SByte,   T); break;
                case ILCode.Conv_Ovf_I2: ImportConv(PrimType.Int16,   T); break;
                case ILCode.Conv_Ovf_I4: ImportConv(PrimType.Int32,   T); break;
                case ILCode.Conv_Ovf_I8: ImportConv(PrimType.Int64,   T); break;
                case ILCode.Conv_Ovf_U1: ImportConv(PrimType.Byte,    T); break;
                case ILCode.Conv_Ovf_U2: ImportConv(PrimType.UInt16,  T); break;
                case ILCode.Conv_Ovf_U4: ImportConv(PrimType.UInt32,  T); break;
                case ILCode.Conv_Ovf_U8: ImportConv(PrimType.UInt64,  T); break;
                case ILCode.Conv_Ovf_I:  ImportConv(PrimType.IntPtr,  T); break;
                case ILCode.Conv_Ovf_U:  ImportConv(PrimType.UIntPtr, T); break;

                case ILCode.Conv_Ovf_I1_Un: ImportConv(PrimType.SByte,   T, T); break;
                case ILCode.Conv_Ovf_I2_Un: ImportConv(PrimType.Int16,   T, T); break;
                case ILCode.Conv_Ovf_I4_Un: ImportConv(PrimType.Int32,   T, T); break;
                case ILCode.Conv_Ovf_I8_Un: ImportConv(PrimType.Int64,   T, T); break;
                case ILCode.Conv_Ovf_U1_Un: ImportConv(PrimType.Byte,    T, T); break;
                case ILCode.Conv_Ovf_U2_Un: ImportConv(PrimType.UInt16,  T, T); break;
                case ILCode.Conv_Ovf_U4_Un: ImportConv(PrimType.UInt32,  T, T); break;
                case ILCode.Conv_Ovf_U8_Un: ImportConv(PrimType.UInt64,  T, T); break;
                case ILCode.Conv_Ovf_I_Un:  ImportConv(PrimType.IntPtr,  T, T); break;
                case ILCode.Conv_Ovf_U_Un:  ImportConv(PrimType.UIntPtr, T, T); break;
                #endregion

                #region Branching
                case ILCode.Br:
                case ILCode.Br_S:
                    ImportBranch(ref inst);
                    break;
                case ILCode.Brfalse:
                case ILCode.Brfalse_S:
                    ImportUnaryBranch(ref inst, CompareOp.Eq, CompareOp.FOeq);
                    break;
                case ILCode.Brtrue:
                case ILCode.Brtrue_S:
                    ImportUnaryBranch(ref inst, CompareOp.Ne, CompareOp.FUne);
                    break;
                case ILCode.Beq_S:
                case ILCode.Beq:
                    ImportBinaryBranch(ref inst, CompareOp.Eq, CompareOp.FOeq);
                    break;
                case ILCode.Bge_S:
                case ILCode.Bge:
                    ImportBinaryBranch(ref inst, CompareOp.Sge, CompareOp.FOge);
                    break;
                case ILCode.Bgt_S:
                case ILCode.Bgt:
                    ImportBinaryBranch(ref inst, CompareOp.Sgt, CompareOp.FOgt);
                    break;
                case ILCode.Ble_S:
                case ILCode.Ble:
                    ImportBinaryBranch(ref inst, CompareOp.Sle, CompareOp.FOle);
                    break;
                case ILCode.Blt_S:
                case ILCode.Blt:
                    ImportBinaryBranch(ref inst, CompareOp.Slt, CompareOp.FOlt);
                    break;
                case ILCode.Bne_Un_S:
                case ILCode.Bne_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ne, CompareOp.FUne);
                    break;
                case ILCode.Bge_Un_S:
                case ILCode.Bge_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Uge, CompareOp.FUge);
                    break;
                case ILCode.Bgt_Un_S:
                case ILCode.Bgt_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ugt, CompareOp.FUgt);
                    break;
                case ILCode.Ble_Un_S:
                case ILCode.Ble_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ule, CompareOp.FUle);
                    break;
                case ILCode.Blt_Un_S:
                case ILCode.Blt_Un:
                    ImportBinaryBranch(ref inst, CompareOp.Ult, CompareOp.FUlt);
                    break;

                case ILCode.Switch: ImportSwitch(ref inst); break;
                #endregion

                #region Comparison
                case ILCode.Ceq:    ImportCompare(CompareOp.Eq, CompareOp.FOeq); break;
                case ILCode.Cgt:    ImportCompare(CompareOp.Sgt, CompareOp.FOgt); break;
                case ILCode.Clt:    ImportCompare(CompareOp.Slt, CompareOp.FOlt); break;
                case ILCode.Cgt_Un: ImportCompare(CompareOp.Ugt, CompareOp.FUgt); break;
                case ILCode.Clt_Un: ImportCompare(CompareOp.Ult, CompareOp.FUlt); break;
                #endregion

                #region Load/Store Array Element
                case ILCode.Ldlen: ImportLoadLen(); break;
                case ILCode.Ldelema: ImportLoadElemAddr((TypeDesc)inst.Operand!); break;

                case ILCode.Ldelem_I1:  ImportLoadElem(PrimType.SByte); break;
                case ILCode.Ldelem_I2:  ImportLoadElem(PrimType.Int16); break;
                case ILCode.Ldelem_I4:  ImportLoadElem(PrimType.Int32); break;
                case ILCode.Ldelem_I8:  ImportLoadElem(PrimType.Int64); break;
                case ILCode.Ldelem_U1:  ImportLoadElem(PrimType.Byte); break;
                case ILCode.Ldelem_U2:  ImportLoadElem(PrimType.UInt16); break;
                case ILCode.Ldelem_U4:  ImportLoadElem(PrimType.UInt32); break;
                case ILCode.Ldelem_R4:  ImportLoadElem(PrimType.Single); break;
                case ILCode.Ldelem_R8:  ImportLoadElem(PrimType.Double); break;
                case ILCode.Ldelem_I:   ImportLoadElem(PrimType.IntPtr); break;
                case ILCode.Ldelem:     ImportLoadElem(null); break;
                case ILCode.Ldelem_Ref: ImportLoadElem((TypeDesc)inst.Operand!); break;

                case ILCode.Stelem_I1:  ImportStoreElem(PrimType.SByte); break;
                case ILCode.Stelem_I2:  ImportStoreElem(PrimType.Int16); break;
                case ILCode.Stelem_I4:  ImportStoreElem(PrimType.Int32); break;
                case ILCode.Stelem_I8:  ImportStoreElem(PrimType.Int64); break;
                case ILCode.Stelem_R4:  ImportStoreElem(PrimType.Single); break;
                case ILCode.Stelem_R8:  ImportStoreElem(PrimType.Double); break;
                case ILCode.Stelem_I:   ImportStoreElem(PrimType.IntPtr); break;
                case ILCode.Stelem_Ref: ImportStoreElem(null); break;
                case ILCode.Stelem:     ImportStoreElem((TypeDesc)inst.Operand!); break;
                #endregion

                #region Load/Store Indirect
                case ILCode.Ldind_I1: ImportLoadInd(PrimType.SByte); break;
                case ILCode.Ldind_I2: ImportLoadInd(PrimType.Int16); break;
                case ILCode.Ldind_I4: ImportLoadInd(PrimType.Int32); break;
                case ILCode.Ldind_I8: ImportLoadInd(PrimType.Int64); break;
                case ILCode.Ldind_U1: ImportLoadInd(PrimType.Byte); break;
                case ILCode.Ldind_U2: ImportLoadInd(PrimType.UInt16); break;
                case ILCode.Ldind_U4: ImportLoadInd(PrimType.UInt32); break;
                case ILCode.Ldind_R4: ImportLoadInd(PrimType.Single); break;
                case ILCode.Ldind_R8: ImportLoadInd(PrimType.Double); break;
                case ILCode.Ldind_I:  ImportLoadInd(PrimType.IntPtr); break;
                case ILCode.Ldind_Ref: ImportLoadInd(null); break;

                case ILCode.Stind_I1: ImportStoreInd(PrimType.SByte); break;
                case ILCode.Stind_I2: ImportStoreInd(PrimType.Int16); break;
                case ILCode.Stind_I4: ImportStoreInd(PrimType.Int32); break;
                case ILCode.Stind_I8: ImportStoreInd(PrimType.Int64); break;
                case ILCode.Stind_R4: ImportStoreInd(PrimType.Single); break;
                case ILCode.Stind_R8: ImportStoreInd(PrimType.Double); break;
                case ILCode.Stind_I:  ImportStoreInd(PrimType.IntPtr); break;
                case ILCode.Stind_Ref: ImportStoreInd(null); break;

                case ILCode.Ldobj: ImportLoadInd((TypeDesc)inst.Operand!); break;
                case ILCode.Stobj: ImportStoreInd((TypeDesc)inst.Operand!); break;
                #endregion

                #region Load/Store Field
                case ILCode.Ldfld:
                case ILCode.Ldsfld:
                    ImportLoadField((FieldDesc)inst.Operand!, opcode == ILCode.Ldsfld);
                    break;

                case ILCode.Stfld:
                case ILCode.Stsfld:
                    ImportStoreField((FieldDesc)inst.Operand!, opcode == ILCode.Stsfld);
                    break;
                
                case ILCode.Ldflda:
                case ILCode.Ldsflda:
                    ImportFieldAddr((FieldDesc)inst.Operand!, opcode == ILCode.Ldsflda);
                    break;
                #endregion

                #region Prefixes
                case ILCode.Unaligned_:     prefix = InstFlags.Unaligned; break;
                case ILCode.Volatile_:      prefix = InstFlags.Volatile; break;
                case ILCode.Tail_:          prefix = InstFlags.Tailcall; break;
                case ILCode.Constrained_:   prefix = InstFlags.Constrained; _callConstraint = (TypeDesc)inst.Operand!; break;
                case ILCode.Readonly_:      prefix = InstFlags.Readonly; break;
                case ILCode.No_: {
                    int flags = (int)inst.Operand!;
                    prefix = (InstFlags)(flags << (int)InstFlags.NoPrefixShift_);
                    break;
                }
                #endregion

                #region Exception Leave/End/Throw
                case ILCode.Leave:
                case ILCode.Leave_S:
                    ImportLeave((int)inst.Operand!);
                    break;
                case ILCode.Throw:
                case ILCode.Rethrow:
                    ImportThrow(opcode == ILCode.Rethrow);
                    break;
                case ILCode.Endfinally:
                case ILCode.Endfilter:
                    ImportContinue(opcode == ILCode.Endfilter);
                    break;
                #endregion

                #region Call
                case ILCode.Call:
                case ILCode.Callvirt:
                    ImportCall((MethodDesc)inst.Operand!, opcode == ILCode.Callvirt);
                    break;
                case ILCode.Ldftn:
                case ILCode.Ldvirtftn:
                    ImportLoadFuncPtr((MethodDesc)inst.Operand!, opcode == ILCode.Ldvirtftn);
                    break;

                case ILCode.Newobj: ImportNewObj((MethodDesc)inst.Operand!); break;
                #endregion

                #region Intrinsics
                case ILCode.Newarr:
                    ImportNewArray((TypeDesc)inst.Operand!);
                    break;
                case ILCode.Ldtoken:
                    ImportLoadToken((EntityDesc)inst.Operand!);
                    break;
                case ILCode.Isinst:
                    ImportIsInst((TypeDesc)inst.Operand!);
                    break;
                case ILCode.Castclass:
                    ImportCast((TypeDesc)inst.Operand!);
                    break;
                #endregion

                case ILCode.Ret: ImportRet(); break;
                case ILCode.Dup: ImportDup(); break;
                case ILCode.Pop: ImportPop(); break;

                case ILCode.Nop:
                case ILCode.Break:
                    break;
                    
                default: throw new NotImplementedException("Opcode " + opcode);
            }
            #pragma warning restore format
            //Update prefix
            if (prefix != InstFlags.None) {
                _prefixFlags |= prefix;
            } else {
                _prefixFlags = InstFlags.None;
                _callConstraint = null;
            }
        }
        //Fallthrough the next block
        if (!code[^1].OpCode.IsTerminator()) {
            var succ = AddSucc(code[^1].GetEndOffset());
            TerminateBlock(new BranchInst(succ));
        }
    }

    private void ImportPop()
    {
        Pop();
    }
    private void ImportDup()
    {
        var val = Pop();
        //Push directly into Stack to avoid duplicating the code
        _stack.Push(val);
        _stack.Push(val);
    }

    private void ImportConst(Const cons)
    {
        Push(cons);
    }

    private void ImportVarLoad(int varIndex, bool isArg = false)
    {
        var variable = GetVar(varIndex, isArg, VarFlags.Loaded);
        Push(new LoadVarInst(variable));
    }
    private void ImportVarStore(int varIndex, bool isArg = false)
    {
        var value = Pop();
        var variable = GetVar(varIndex, isArg, VarFlags.Stored);
        Emit(new StoreVarInst(variable, value));
    }
    private void ImportVarAddr(int varIndex, bool isArg = false)
    {
        var variable = GetVar(varIndex, isArg, VarFlags.AddrTaken);
        Push(new VarAddrInst(variable));
    }
    private Variable GetVar(int index, bool isArg, VarFlags flagsToAdd)
    {
        var variable = isArg ? _importer._argSlots[index] : _body.Definition.ILBody!.Locals[index];
        ref var flags = ref _importer._varFlags[index + (isArg ? 0 : _body.Args.Length)];
        flags |= flagsToAdd | (_activeGuard != null ? VarFlags.UsedInsideTry : VarFlags.UsedOutsideTry);

        if ((flags & VarFlags.CrossesTry) == VarFlags.CrossesTry || (flags & VarFlags.AddrTaken) != 0) {
            variable.IsExposed = true;
        }
        return variable;
    }

    private void ImportBinary(BinaryOp op, BinaryOp opFlt = (BinaryOp)(-1))
    {
        var right = Pop();
        var left = Pop();

        if (left.ResultType.StackType == StackType.Float || right.ResultType.StackType == StackType.Float) {
            op = opFlt >= 0 ? opFlt : throw new InvalidProgramException();
        }
        Push(new BinaryInst(op, left, right));
    }

    private void ImportUnary(ILCode code)
    {
        var value = Pop();
        var type = value.ResultType.StackType;

        if (code == ILCode.Not) {
            //Emit `x ^ ~0` for int/long
            if (type is StackType.Int or StackType.Long) {
                Push(new BinaryInst(BinaryOp.Xor, value, ConstInt.Create(value.ResultType, ~0L)));
            } else {
                Push(new UnaryInst(UnaryOp.Not, value));
            }
        } else {
            //Emit `0 - x` for int/long
            if (type is StackType.Int or StackType.Long) {
                Push(new BinaryInst(BinaryOp.Sub, ConstInt.Create(value.ResultType, 0), value));
            } else {
                Push(new UnaryInst(type is StackType.Float ? UnaryOp.FNeg : UnaryOp.Neg, value));
            }
        }
    }

    private void ImportConv(TypeDesc dstType, bool checkOverflow = false, bool srcUnsigned = false)
    {
        var value = Pop();
        Push(new ConvertInst(value, dstType, checkOverflow, srcUnsigned));
    }

    private void ImportCompare(CompareOp op, CompareOp fltOp)
    {
        var right = Pop();
        var left = Pop();
        Push(CreateCompare(op, fltOp, left, right));
    }
    private CompareInst CreateCompare(CompareOp op, CompareOp fltOp, Value left, Value right)
    {
        Assert(left.ResultType.StackType == right.ResultType.StackType); //TODO: check CompareInst operand types
        return new CompareInst(op, left, right);
    }

    private void ImportBranch(ref ILInstruction inst)
    {
        AddBranch(ref inst, null);
    }
    private void ImportUnaryBranch(ref ILInstruction inst, CompareOp op, CompareOp fltOp)
    {
        var val = Pop();
        var zero = Const.CreateZero(val.ResultType);
        AddBranch(ref inst, CreateCompare(op, fltOp, val, zero));
    }
    private void ImportBinaryBranch(ref ILInstruction inst, CompareOp op, CompareOp fltOp)
    {
        var right = Pop();
        var left = Pop();
        AddBranch(ref inst, CreateCompare(op, fltOp, left, right));
    }
    private void AddBranch(ref ILInstruction inst, CompareInst? cond = null)
    {
        var thenBlock = AddSucc((int)inst.Operand!);

        if (cond == null) {
            TerminateBlock(new BranchInst(thenBlock));
        } else {
            var elseBlock = AddSucc(inst.GetEndOffset());

            Emit(cond);
            TerminateBlock(new BranchInst(cond, thenBlock, elseBlock));
        }
    }

    private void ImportSwitch(ref ILInstruction inst)
    {
        var value = Pop();
        var defaultTarget = AddSucc(inst.GetEndOffset());

        var targetOffsets = (int[])inst.Operand!;
        var targets = new BasicBlock[targetOffsets.Length];

        for (int i = 0; i < targets.Length; i++) {
            targets[i] = AddSucc(targetOffsets[i]);
        }
        TerminateBlock(new SwitchInst(value, defaultTarget, targets));
    }

    private void ImportLoadLen()
    {
        var array = Pop();
        Push(new ArrayLenInst(array));
    }
    private void ImportLoadElem(TypeDesc? type)
    {
        var index = Pop();
        var array = Pop();

        if (type == null) {
            type = ((ArrayType)array.ResultType).ElemType; //ldelem_any
        }
        Push(new LoadArrayInst(array, index, type, PopArrayAccFlags()));
    }
    private void ImportStoreElem(TypeDesc? type)
    {
        var value = Pop();
        var index = Pop();
        var array = Pop();

        if (type == null) {
            type = ((ArrayType)array.ResultType).ElemType; //stelem_any
        }
        Emit(new StoreArrayInst(array, index, value, type, PopArrayAccFlags()));
    }
    private void ImportLoadElemAddr(TypeDesc elemType)
    {
        var index = Pop();
        var array = Pop();
        Push(new ArrayAddrInst(array, index, elemType, PopArrayAccFlags()));
    }
    private ArrayAccessFlags PopArrayAccFlags()
    {
        var flags = ArrayAccessFlags.None;
        if (HasPrefix(InstFlags.NoRangeCheck)) flags |= ArrayAccessFlags.NoBoundsCheck;
        if (HasPrefix(InstFlags.NoTypeCheck)) flags |= ArrayAccessFlags.NoTypeCheck;
        if (HasPrefix(InstFlags.NoNullCheck)) flags |= ArrayAccessFlags.NoNullCheck;
        if (HasPrefix(InstFlags.Readonly)) flags |= ArrayAccessFlags.ReadOnly;
        return flags;
    }

    private void ImportLoadInd(TypeDesc? type)
    {
        var addr = Pop();

        if (type == null) {
            Ensure(addr.ResultType is PointerType or ByrefType);
            type = addr.ResultType.ElemType!; //ldind_ref
        }
        Push(new LoadPtrInst(addr, type, PopPointerFlags()));
    }
    private void ImportStoreInd(TypeDesc? type)
    {
        var value = Pop();
        var addr = Pop();

        if (type == null) {
            Ensure(addr.ResultType is PointerType or ByrefType);
            type = addr.ResultType.ElemType!; //stind_ref
        }
        Emit(new StorePtrInst(addr, value, type, PopPointerFlags()));
    }
    private PointerFlags PopPointerFlags()
    {
        var flags = PointerFlags.None;
        if (HasPrefix(InstFlags.Unaligned)) flags |= PointerFlags.Unaligned;
        if (HasPrefix(InstFlags.Volatile)) flags |= PointerFlags.Volatile;
        return flags;
    }

    private void ImportLoadField(FieldDesc field, bool isStatic)
    {
        var obj = field.IsStatic ? null : Pop();
        Push(new LoadFieldInst(field, obj));
    }
    private void ImportStoreField(FieldDesc field, bool isStatic)
    {
        var value = Pop();
        var obj = field.IsStatic ? null : Pop();
        Emit(new StoreFieldInst(field, obj, value));
    }
    private void ImportFieldAddr(FieldDesc field, bool isStatic)
    {
        var obj = field.IsStatic ? null : Pop();
        Push(new FieldAddrInst(field, obj));
    }

    private void ImportCall(MethodDesc method, bool isVirt)
    {
        var args = PopCallArgs(method);
        var constraint = HasPrefix(InstFlags.Constrained) ? _callConstraint : null;
        var inst = new CallInst(method, args, isVirt, constraint);

        if (method.ReturnType.Kind == TypeKind.Void) {
            Emit(inst);
        } else {
            Push(inst);
        }
    }
    private void ImportLoadFuncPtr(MethodDesc method, bool isVirt)
    {
        var obj = isVirt ? Pop() : null;
        Push(new FuncAddrInst(method, obj));
    }

    private void ImportNewObj(MethodDesc ctor)
    {
        var args = PopCallArgs(ctor, true);
        Push(new NewObjInst(ctor, args));
    }
    private Value[] PopCallArgs(MethodDesc method, bool ctor = false)
    {
        var args = new Value[method.Params.Length - (ctor ? 1 : 0)];
        for (int i = args.Length - 1; i >= 0; i--) {
            args[i] = Pop();
        }
        return args;
    }

    private void ImportRet()
    {
        bool isVoid = _body.ReturnType.Kind == TypeKind.Void;
        var value = isVoid ? null : Pop();

        Emit(new ReturnInst(value));
    }

    private void ImportLeave(int targetOffset)
    {
        Ensure(_activeGuard != null, "Cannot leave non protected region");
        var targetBlock = AddSucc(targetOffset);
        TerminateBlock(new LeaveInst(_activeGuard, targetBlock));
    }
    private void ImportContinue(bool isFromFilter)
    {
        Ensure(_activeGuard != null, "Cannot leave non protected region");
        var filterResult = isFromFilter ? Pop() : null;
        TerminateBlock(new ContinueInst(_activeGuard, filterResult));
    }

    private void ImportThrow(bool isRethrow)
    {
        var exception = isRethrow ? null : Pop();
        TerminateBlock(new ThrowInst(exception));
    }

    private void ImportNewArray(TypeDesc elemType)
    {
        var length = Pop();
        var resultType = new ArrayType(elemType);
        Push(new IntrinsicInst(IntrinsicId.NewArray, resultType, length));
    }

    private void ImportLoadToken(EntityDesc entity)
    {
        var sys = _mod.SysTypes;
        var resultType = entity switch {
            MethodDesc => sys.RuntimeMethodHandle,
            FieldDesc  => sys.RuntimeFieldHandle,
            TypeDesc   => sys.RuntimeTypeHandle,
            _ => throw Error("Invalid token type for ldtoken")
        };
        Push(new IntrinsicInst(IntrinsicId.LoadToken, resultType, entity));
    }

    private void ImportIsInst(TypeDesc type)
    {
        Push(new IntrinsicInst(IntrinsicId.IsInstance, PrimType.Bool, Pop(), type));
    }
    private void ImportCast(TypeDesc destType)
    {
        Push(new IntrinsicInst(IntrinsicId.CastClass, destType, Pop()));
    }

    private Exception Error(string? msg = null)
    {
        return new InvalidProgramException(msg);
    }
}

internal enum InstFlags
{
    None            = 0,

    Unaligned       = 1 << 0,
    Volatile        = 1 << 1,
    Tailcall        = 1 << 2,
    Constrained     = 1 << 3,
    Readonly        = 1 << 4,

    //Bits [16..23] are reserved for `no.` prefix
    NoPrefixShift_  = 16,
    NoTypeCheck     = 1 << 16,
    NoRangeCheck    = 1 << 17,
    NoNullCheck     = 1 << 18,
}
internal enum VarFlags
{
    None = 0,
    Loaded          = 1 << 1,
    Stored          = 1 << 2,
    AddrTaken       = 1 << 3,
    UsedInsideTry   = 1 << 4,
    UsedOutsideTry  = 1 << 5,

    CrossesTry = UsedInsideTry | UsedOutsideTry
}