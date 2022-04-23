namespace DistIL.Frontend;

using DistIL.IR;
using DistIL.AsmIO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal class BlockState
{
    readonly ILImporter _importer;
    readonly MethodDef _method;
    ModuleDef _mod => _method.Module;

    public readonly BasicBlock Block;
    private ValueStack _stack;

    /// <summary> Variables that were left in the stack after the execution of a predecessor block. </summary>
    private ValueStack? _entryStrack;
    private List<BlockState> _succStates = new();

    private InstFlags _prefixFlags = InstFlags.None;
    private int _startOffset, _currOffset;

    public BlockState(ILImporter importer, int offset)
    {
        _importer = importer;
        _method = importer.Method;
        Block = _method.CreateBlock();
        _stack = new ValueStack(_method.Body!.MaxStack);

        _startOffset = _currOffset = offset;
    }

    private void Emit(Instruction inst)
    {
        inst.ILOffset = _currOffset;
        Block.InsertLast(inst);
    }

    private void Push(Value value)
    {
        if (value is Instruction inst) {
            Emit(inst);
        }
        _stack.Push(value);
    }
    private Value Pop()
    {
        return _stack.Pop();
    }

    private bool HasPrefix(InstFlags flag)
    {
        return _prefixFlags.HasFlag(flag);
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
        if (_stack.Count == 0) return;

        //Allocate temp variables to store the spilled values, 
        //or reuse from another predecessor of a successor
        var temps = _succStates
                       .Select(s => s._entryStrack)
                       .FirstOrDefault(s => s != null);

        if (temps == null) {
            temps = new ValueStack(_stack.Count);
            foreach (var value in _stack) {
                temps.Push(new Variable(value.ResultType));
            }
        } else {
            //FIXME: III.1.8.1.3
            var types1 = temps.Select(v => v.ResultType.StackType);
            var types2 = _stack.Select(v => v.ResultType.StackType);
            if (!types1.SequenceEqual(types2)) {
                throw Error("Inconsistent evaluation stack between basic blocks.");
            }
        }
        //Copy spilled values to temps
        for (int i = 0; i < _stack.Count; i++) {
            if (temps[i] != _stack[i]) {
                Emit(new StoreVarInst((Variable)temps[i], _stack[i]));
            }
        }
        //Propagate to successors
        foreach (var succ in _succStates) {
            if (succ._entryStrack != null) {
                Assert(succ._entryStrack.SequenceEqual(temps));
                continue;
            }
            succ._entryStrack = temps;
            foreach (Variable var in temps) {
                succ.Push(new LoadVarInst(var));
            }
        }
    }

    public static bool IsTerminator(ref ILInstruction inst)
    {
        return inst.FlowControl is
            ILFlowControl.Branch or
            ILFlowControl.CondBranch or
            ILFlowControl.Return;
    }

    /// <summary>
    /// Translates the IL code into IR instructions. 
    /// `code` contains all instructions in the method, but only the range `start`-`end` is imported.
    /// </summary>
    public void ImportCode(Span<ILInstruction> code, int start, int end)
    {
        foreach (ref var inst in code[start..end]) {
            _currOffset = inst.Offset;
            var prefix = InstFlags.None;
            var opcode = inst.OpCode;

            #pragma warning disable format
            const bool T = true, F = false;

            switch (opcode) {
                #region Load Const
                case >= ILCode.Ldc_I4_M1 and <= ILCode.Ldc_I4_8:
                    ImportConst(ConstInt.CreateI(opcode - ILCode.Ldc_I4_0));
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
                case ILCode.Add:    ImportBinary(ILCode.Add, BinaryOp.Add, BinaryOp.FAdd); break;
                case ILCode.Sub:    ImportBinary(ILCode.Sub, BinaryOp.Sub, BinaryOp.FSub); break;
                case ILCode.Mul:    ImportBinary(ILCode.Mul, BinaryOp.Mul, BinaryOp.FMul); break;
                case ILCode.Div:    ImportBinary(ILCode.Div, BinaryOp.SDiv, BinaryOp.FDiv); break;
                case ILCode.Rem:    ImportBinary(ILCode.Rem, BinaryOp.SRem, BinaryOp.FRem); break;
                case ILCode.Div_Un: ImportBinary(ILCode.Div, BinaryOp.UDiv); break;
                case ILCode.Rem_Un: ImportBinary(ILCode.Rem, BinaryOp.URem); break;

                case ILCode.And:    ImportBinary(ILCode.And, BinaryOp.And); break;
                case ILCode.Or:     ImportBinary(ILCode.Or,  BinaryOp.Or); break;
                case ILCode.Xor:    ImportBinary(ILCode.Xor, BinaryOp.Xor); break;
                case ILCode.Shl:    ImportBinary(ILCode.Shl, BinaryOp.Shl); break;
                case ILCode.Shr:    ImportBinary(ILCode.Shr, BinaryOp.Shra); break;
                case ILCode.Shr_Un: ImportBinary(ILCode.Shr_Un, BinaryOp.Shrl); break;

                case ILCode.Add_Ovf:      ImportBinaryOvf(ILCode.Add, BinaryOp.AddOvf); break;
                case ILCode.Add_Ovf_Un:   ImportBinaryOvf(ILCode.Add, BinaryOp.UAddOvf); break;
                case ILCode.Sub_Ovf:      ImportBinaryOvf(ILCode.Sub, BinaryOp.SubOvf); break;
                case ILCode.Sub_Ovf_Un:   ImportBinaryOvf(ILCode.Sub, BinaryOp.USubOvf); break;
                case ILCode.Mul_Ovf:      ImportBinaryOvf(ILCode.Mul, BinaryOp.MulOvf); break;
                case ILCode.Mul_Ovf_Un:   ImportBinaryOvf(ILCode.Mul, BinaryOp.UMulOvf); break;

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
                    ImportUnaryBranch(ref inst, CompareOp.Ne, CompareOp.FOne);
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
                    ImportBinaryBranch(ref inst, CompareOp.Ne, CompareOp.FOne);
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
                case ILCode.Ldelema: ImportLoadElemAddr((RType)inst.Operand!); break;

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
                case ILCode.Ldelem_Ref: ImportLoadElem((RType)inst.Operand!); break;

                case ILCode.Stelem_I1:  ImportStoreElem(PrimType.SByte); break;
                case ILCode.Stelem_I2:  ImportStoreElem(PrimType.Int16); break;
                case ILCode.Stelem_I4:  ImportStoreElem(PrimType.Int32); break;
                case ILCode.Stelem_I8:  ImportStoreElem(PrimType.Int64); break;
                case ILCode.Stelem_R4:  ImportStoreElem(PrimType.Single); break;
                case ILCode.Stelem_R8:  ImportStoreElem(PrimType.Double); break;
                case ILCode.Stelem_I:   ImportStoreElem(PrimType.IntPtr); break;
                case ILCode.Stelem_Ref: ImportStoreElem(null); break;
                case ILCode.Stelem:     ImportStoreElem((RType)inst.Operand!); break;
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

                case ILCode.Ldobj: ImportLoadInd((RType)inst.Operand!); break;
                case ILCode.Stobj: ImportStoreInd((RType)inst.Operand!); break;
                #endregion

                #region Load/Store Field
                case ILCode.Ldfld:
                case ILCode.Ldsfld:
                    ImportLoadField((Field)inst.Operand!, opcode == ILCode.Ldsfld);
                    break;

                case ILCode.Stfld:
                case ILCode.Stsfld:
                    ImportStoreField((Field)inst.Operand!, opcode == ILCode.Stsfld);
                    break;
                #endregion

                #region Prefixes
                case ILCode.Unaligned_:     prefix = InstFlags.Unaligned; break;
                case ILCode.Volatile_:      prefix = InstFlags.Volatile; break;
                case ILCode.Tail_:          prefix = InstFlags.Tailcall; break;
                case ILCode.Constrained_:   prefix = InstFlags.Constrained; break;
                case ILCode.Readonly_:      prefix = InstFlags.Readonly; break;
                case ILCode.No_: {
                    int flags = (int)inst.Operand!;
                    prefix = (InstFlags)(flags << (int)InstFlags.NoPrefixShift_);
                    break;
                }
                #endregion

                #region Intrinsics
                case ILCode.Newarr:
                    ImportNewArray((RType)inst.Operand!);
                    break;

                case ILCode.Ldtoken:
                    ImportLoadToken((EntityDef)inst.Operand!);
                    break;

                #endregion

                case ILCode.Ret: ImportRet(); break;
                case ILCode.Dup: ImportDup(); break;
                case ILCode.Pop: ImportPop(); break;

                case ILCode.Call:
                case ILCode.Callvirt:
                    ImportCall((Callsite)inst.Operand!, opcode == ILCode.Callvirt);
                    break;

                case ILCode.Newobj:     ImportNewObj((Callsite)inst.Operand!); break;

                case ILCode.Switch:     ImportSwitch(ref inst); break;

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
            }
        }
        //Fallthrough the next block
        if (!IsTerminator(ref code[end - 1])) {
            var succ = AddSucc(code[end].Offset);
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
        var variable = _importer.GetVar(varIndex, isArg);
        Push(new LoadVarInst(variable));
    }
    private void ImportVarStore(int varIndex, bool isArg = false)
    {
        var value = Pop();
        var variable = _importer.GetVar(varIndex, isArg);
        Emit(new StoreVarInst(variable, value));
    }
    private void ImportVarAddr(int varIndex, bool isArg = false)
    {
        var variable = _importer.GetVar(varIndex, isArg);
        Push(new VarAddrInst(variable));
    }

    private void ImportBinary(ILCode code, BinaryOp op, BinaryOp opFlt = (BinaryOp)(-1))
    {
        var right = Pop();
        var left = Pop();

        var resultType = GetResultType(code, left.ResultType, right.ResultType)
            ?? throw new InvalidProgramException();

        //TODO: convert left,right to resultType (BinaryInst only accepts operands of the same type)
        //TODO: unsigned types
        if (resultType.StackType == StackType.Float) {
            op = opFlt >= 0 ? opFlt : throw new InvalidProgramException();
        }
        Push(new BinaryInst(op, left, right));

        static RType? GetResultType(ILCode code, RType a, RType b)
        {
            //ECMA335 III.1.5
            var sa = a.StackType;
            var sb = b.StackType;

            if (sa == StackType.Int) {
                //int : int = int
                //int : nint = nint
                //int : & = &
                return sb == StackType.Int ? a : 
                       code == ILCode.Add && (sb == StackType.NInt || sb == StackType.ByRef) ? b : null;
            }
            if (sa == StackType.Long || sa == StackType.Float) {
                //int64 : int64 = int64
                //float : float = float
                return sb == sa ? a : null;
            }
            if (sa == StackType.NInt) {
                //nint : int = nint
                //nint : nint = nint
                //nint + & = &
                return sb == StackType.NInt || sb == StackType.Int ? a :
                       code == ILCode.Add && sb == StackType.ByRef ? b : null;
            }
            if (sa == StackType.ByRef) {
                //& +- int = &
                //& +- nint = &
                //& - & = nint
                return (sb == StackType.NInt || sb == StackType.Int) && (code == ILCode.Add || code == ILCode.Sub) ? a :
                       sb == StackType.ByRef && code == ILCode.Sub ? PrimType.IntPtr : null;
            }
            return null;
        }
    }
    private void ImportBinaryOvf(ILCode code, BinaryOp op)
    {
        var right = Pop();
        var left = Pop();

        Assert(left.ResultType == right.ResultType); //TODO: check BinaryInst overflow operand types
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

    private void ImportConv(RType dstType, bool checkOverflow = false, bool srcUnsigned = false)
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
    private void ImportLoadElem(RType? type)
    {
        var index = Pop();
        var array = Pop();

        if (type == null) {
            type = ((ArrayType)array.ResultType).ElemType; //ldelem_any
            Assert(type.StackType == StackType.Object);
        }
        Push(new LoadArrayInst(array, index, type, PopArrayAccFlags()));
    }
    private void ImportStoreElem(RType? type)
    {
        var value = Pop();
        var index = Pop();
        var array = Pop();

        if (type == null) {
            type = ((ArrayType)array.ResultType).ElemType; //stelem_any
            Assert(type.StackType == StackType.Object);
        }
        Emit(new StoreArrayInst(array, index, value, type, PopArrayAccFlags()));
    }
    private void ImportLoadElemAddr(RType elemType)
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

    private void ImportLoadInd(RType? type)
    {
        var addr = Pop();

        if (type == null) {
            Ensure(addr.ResultType is PointerType or ByrefType);
            type = addr.ResultType.ElemType!; //ldind_ref
            Assert(type.StackType == StackType.Object);
        }
        Push(new LoadPtrInst(addr, type, PopPointerFlags()));
    }
    private void ImportStoreInd(RType? type)
    {
        var value = Pop();
        var addr = Pop();

        if (type == null) {
            Ensure(addr.ResultType is PointerType or ByrefType);
            type = addr.ResultType.ElemType!; //stind_ref
            Assert(type.StackType == StackType.Object);
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

    private void ImportLoadField(Field field, bool isStatic)
    {
        var obj = field.IsStatic ? null : Pop();
        Push(new LoadFieldInst(field, obj));
    }
    private void ImportStoreField(Field field, bool isStatic)
    {
        var value = Pop();
        var obj = field.IsStatic ? null : Pop();
        Emit(new StoreFieldInst(field, obj, value));
    }

    private void ImportCall(Callsite method, bool isVirt)
    {
        var args = PopCallArgs(method);
        var inst = new CallInst(method, args, isVirt);

        if (method.RetType.Kind == TypeKind.Void) {
            Emit(inst);
        } else {
            Push(inst);
        }
    }
    private void ImportNewObj(Callsite ctor)
    {
        var args = PopCallArgs(ctor, true);
        Push(new NewObjInst(ctor, args));
    }
    private Value[] PopCallArgs(Callsite method, bool ctor = false)
    {
        var args = new Value[method.NumArgs - (ctor ? 1 : 0)];
        for (int i = args.Length - 1; i >= 0; i--) {
            args[i] = Pop();
        }
        return args;
    }

    private void ImportRet()
    {
        bool isVoid = _method.RetType.Kind == TypeKind.Void;
        var value = isVoid ? null : Pop();

        Emit(new ReturnInst(value));
    }

    private void ImportNewArray(RType elemType)
    {
        var length = Pop();
        var lengthType = length.ResultType.StackType switch {
            StackType.Int => PrimType.Int32,
            StackType.NInt => PrimType.IntPtr,
            _ => throw Error("Invalid argument type for newarr")
        };
        var type = new ArrayType(elemType);
        var intrinsic = new Intrinsic(IntrinsicId.NewArray, type, lengthType);
        Push(new CallInst(intrinsic, new[] { length }));
    }

    private void ImportLoadToken(EntityDef entity)
    {
        var token = entity.Handle;
        var handleType = token.Kind switch {
            HandleKind.MethodDefinition or
            HandleKind.MethodSpecification
                => typeof(RuntimeMethodHandle),
            HandleKind.FieldDefinition
                => typeof(RuntimeFieldHandle),
            HandleKind.TypeDefinition or
            HandleKind.TypeReference
                => typeof(RuntimeTypeHandle),
            HandleKind.MemberReference => GetMemberHandleType(token),
            _ => throw Error("Invalid token type for ldtoken")
        };
        var rawToken = ConstInt.Create(PrimType.UInt32, MetadataTokens.GetToken(token));
        var type = _mod.Import(handleType) ?? throw Error();
        var intrinsic = new Intrinsic(IntrinsicId.LoadToken, type, PrimType.UInt32);
        Push(new CallInst(intrinsic, new[] { rawToken }));
    }

    private Type GetMemberHandleType(Handle token)
    {
        var entity = _mod.Reader.GetMemberReference((MemberReferenceHandle)token);
        return entity.GetKind() == MemberReferenceKind.Method 
            ? typeof(RuntimeMethodHandle) 
            : typeof(RuntimeFieldHandle);
    }

    private Exception Error(string? msg = null)
    {
        return new InvalidProgramException(msg);
    }
}