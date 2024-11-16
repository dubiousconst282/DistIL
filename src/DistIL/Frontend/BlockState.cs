namespace DistIL.Frontend;

internal class BlockState
{
    readonly ILImporter _importer;
    readonly MethodBody _body;
    public readonly int StartOffset;

    private ModuleDef _mod => _body.Definition.Module;

    public readonly BasicBlock Block;
    // A parent block that enters this one. Currently used to handle nested exception regions.
    public BasicBlock EntryBlock;

    readonly ArrayStack<Value> _stack;
    readonly List<BlockState> _preds = new();

    private InstFlags _prefixFlags = InstFlags.None;
    private TypeDesc? _callConstraint;

    private DebugSourceLocation? _currDebugLoc = null;

    public BlockState(ILImporter importer, int startOffset)
    {
        _importer = importer;
        _body = importer._body;
        this.StartOffset = startOffset;
        
        Block = _body.CreateBlock();
        EntryBlock = Block;

        var ilBody = importer._method.ILBody!;
        _stack = new ArrayStack<Value>(ilBody.MaxStack);
    }

    public void Emit(Instruction inst)
    {
        Block.InsertLast(inst);
        inst.DebugLoc = _currDebugLoc;
    }

    public void PushNoEmit(Value value)
    {
        Debug.Assert(value.HasResult);
        _stack.Push(value);
    }
    public void Push(Value value)
    {
        if (value is Instruction inst) {
            Emit(inst);
        }
        if (value.HasResult) {
            _stack.Push(value);
        }
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
        succ._preds.Add(this);
        return succ.EntryBlock;
    }
    // Adds the last instruction in the block (a branch).
    private void TerminateBlock(Instruction branch, bool clearStack = false)
    {
        Emit(branch);

        if (clearStack) {
            _stack.Clear();
        }
    }

    private void MergePredStacks()
    {
        if (_preds.Count == 0) return;

        // Phis are not necessary if there's only one pred
        if (_preds.Count == 1) {
            foreach (var value in _preds[0]._stack) {
                _stack.Push(value);
            }
            return;
        }
        int maxDepth = _preds[0]._stack.Count;

        // We don't make any guarantees for invalid IL, this is just for good measure.
        Debug.Assert(_preds.All(b => b._stack.Count == maxDepth));

        if (maxDepth == 0) return;

        for (int depth = 0; depth < maxDepth; depth++) {
            var args = new PhiArg[_preds.Count];
            int argIdx = 0;
            var type = default(TypeDesc);
            bool allSameArg = true;

            foreach (var pred in _preds) {
                if (pred._stack.Count != maxDepth) throw Fail();

                var value = pred._stack[depth];
                args[argIdx++] = (pred.Block, value);

                if (value is not ConstNull) {
                    type = GetMergedStackType(type, value.ResultType) ?? throw Fail();
                }
                allSameArg &= argIdx < 2 || args[argIdx - 2].Value.Equals(value);
            }
            type ??= PrimType.Object; // all args were ConstNull`s

            var result = allSameArg
                ? args[0].Value
                : EntryBlock.InsertPhi(new PhiInst(type, args));

            _stack.Push(result);
        }

        Exception Fail() => throw Error("Inconsistent evaluation stack between basic blocks.");
    }

    private TypeDesc? GetMergedStackType(TypeDesc? currType, TypeDesc newType)
    {
        // III.1.8.1.3
        if (currType == null) {
            return newType;
        }
        if (newType.IsAssignableTo(currType)) {
            return currType;
        }
        if (currType.IsAssignableTo(newType)) {
            return newType;
        }
        if (currType.StackType is StackType.Object && newType.StackType is StackType.Object) {
            return TypeDesc.GetCommonAncestor(currType, newType);
        }
        return null;
    }

    /// <summary> Translates the IL code into IR instructions. </summary>
    public void ImportCode(Span<ILInstruction> code)
    {
        MergePredStacks();
        var seqPoints = _importer._debugSyms?.SequencePoints;
        int seqIndex = _importer._debugSyms?.IndexOfSequencePoint(code[0].Offset) ?? -1;

        foreach (ref var inst in code) {
            // Update debug location info
            if (seqPoints != null) {
                while (seqIndex + 1 < seqPoints.Count &&  inst.Offset >= seqPoints[seqIndex + 1].Offset) {
                    seqIndex++;
                }
                var sp = seqPoints[seqIndex];

                if (sp.IsHidden) {
                    _currDebugLoc = null;
                } else if (_currDebugLoc == null || !SequencePoint.Create(_currDebugLoc, 0).IsSameSourceRange(sp)) {
                    _currDebugLoc = new DebugSourceLocation(sp);
                }
            }
            var prefix = InstFlags.None;
            var opcode = inst.OpCode;

            #pragma warning disable format
            const bool T = true, F = false;

            switch (opcode) {
                #region Load Const
                case >= ILCode.Ldc_I4_M1 and <= ILCode.Ldc_I4_8:
                    ImportConst(ConstInt.CreateI((int)opcode - (int)ILCode.Ldc_I4_0));
                    break;
                case ILCode.Ldc_I4_S or ILCode.Ldc_I4:
                    ImportConst(ConstInt.CreateI((int)inst.Operand!));
                    break;
                case ILCode.Ldc_I8: ImportConst(ConstInt.CreateL((long)inst.Operand!)); break;
                case ILCode.Ldc_R4: ImportConst(ConstFloat.CreateS((float)inst.Operand!)); break;
                case ILCode.Ldc_R8: ImportConst(ConstFloat.CreateD((double)inst.Operand!)); break;
                case ILCode.Ldnull: ImportConst(ConstNull.Create()); break;
                case ILCode.Ldstr:  ImportConst(ConstString.Create((string)inst.Operand!)); break;
                #endregion

                #region Load/Store Local/Argument
                case >= ILCode.Ldarg_0 and <= ILCode.Ldarg_3:
                case >= ILCode.Ldloc_0 and <= ILCode.Ldloc_3:
                case >= ILCode.Stloc_0 and <= ILCode.Stloc_3:
                case ILCode.Ldarg_S or ILCode.Ldarg:
                case ILCode.Ldloc_S or ILCode.Ldloc:
                case ILCode.Starg_S or ILCode.Starg:
                case ILCode.Stloc_S or ILCode.Stloc:
                case ILCode.Ldarga_S or ILCode.Ldarga:
                case ILCode.Ldloca_S or ILCode.Ldloca:
                    ImportVarInst(ref inst);
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
                case ILCode.Conv_R_Un: ImportConv(PrimType.Double, F, T); break;

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
                case ILCode.Ldelem_Ref: ImportLoadElem(null); break;
                case ILCode.Ldelem:     ImportLoadElem((TypeDesc)inst.Operand!); break;

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
                    ImportResume(opcode == ILCode.Endfilter);
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
                    Push(new CilIntrinsic.NewArray((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Castclass:
                    Push(new CilIntrinsic.CastClass((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Isinst:
                    Push(new CilIntrinsic.AsInstance((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Box:
                    Push(new CilIntrinsic.Box((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Unbox:
                    Push(new CilIntrinsic.UnboxRef((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Unbox_Any:
                    Push(new CilIntrinsic.UnboxObj((TypeDesc)inst.Operand!, Pop()));
                    break;
                case ILCode.Ldtoken:
                    Push(new CilIntrinsic.LoadHandle(_mod.Resolver, (EntityDesc)inst.Operand!));
                    break;
                case ILCode.Initobj:
                    Emit(new CilIntrinsic.MemSet(Pop(), (TypeDesc)inst.Operand!, PopPointerFlags()));
                    break;
                case ILCode.Initblk:
                    Emit(new CilIntrinsic.MemSet(numBytes: Pop(), value: Pop(), destPtr: Pop(), flags: PopPointerFlags()));
                    break;
                case ILCode.Cpobj:
                    Emit(new CilIntrinsic.MemCopy(srcPtr: Pop(), destPtr: Pop(), type: (TypeDesc)inst.Operand!, flags: PopPointerFlags()));
                    break;
                case ILCode.Cpblk:
                    Emit(new CilIntrinsic.MemCopy(numBytes: Pop(), srcPtr: Pop(), destPtr: Pop(), flags: PopPointerFlags()));
                    break;
                case ILCode.Sizeof:
                    Push(new CilIntrinsic.SizeOf((TypeDesc)inst.Operand!));
                    break;
                case ILCode.Localloc:
                    Push(new CilIntrinsic.Alloca(Pop()));
                    break;
                case ILCode.Ckfinite:
                    Push(new CilIntrinsic.CheckFinite(value: Pop()));
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
            // Update prefix
            if (prefix != InstFlags.None) {
                _prefixFlags |= prefix;
            } else {
                _prefixFlags = InstFlags.None;
                _callConstraint = null;
            }
        }
        // Fallthrough the next block
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
        _stack.Push(val);
        _stack.Push(val);
    }

    private void ImportConst(Const cons)
    {
        Push(cons);
    }

    private void ImportVarInst(ref ILInstruction inst)
    {
        var (op, index) = ILImporter.GetVarInstOp(inst.OpCode, inst.Operand);
        var (slot, flags, isBlockLocal) = _importer.GetVarSlot(op, index);

        // Arguments that are only ever loaded don't need variables
        if (!isBlockLocal && slot is Argument) {
            Debug.Assert((flags | op) == (VarFlags.IsArg | VarFlags.Loaded));
            Push(slot);
            return;
        }
        Debug.Assert(isBlockLocal || slot is LocalSlot);

        switch (op & VarFlags.OpMask) {
            case VarFlags.Loaded: {
                if (isBlockLocal) {
                    PushNoEmit(slot);
                } else if (Block.Last is LoadInst prevLoad && prevLoad.Address == slot) {
                    PushNoEmit(prevLoad);
                } else {
                    Push(new LoadInst(slot));
                }
                break;
            }
            case VarFlags.Stored: {
                if (isBlockLocal) {
                    _importer.SetBlockLocalVarSlot(index, Pop());
                } else {
                    Emit(new StoreInst(slot, Pop()));
                }
                break;
            }
            case VarFlags.AddrTaken: {
                Push(slot);
                break;
            }
            default: throw new UnreachableException();
        }
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

        var op = code switch {
            ILCode.Not => UnaryOp.Not,
            ILCode.Neg => type == StackType.Float ? UnaryOp.FNeg : UnaryOp.Neg
        };
        Push(new UnaryInst(op, value));
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
    private static CompareInst CreateCompare(CompareOp op, CompareOp fltOp, Value left, Value right)
    {
        bool isFloat = left.ResultType.Kind.IsFloat() || right.ResultType.Kind.IsFloat();
        return new CompareInst(isFloat ? fltOp : op, left, right);
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
        Push(new CilIntrinsic.ArrayLen(array));
    }
    private void ImportLoadElem(TypeDesc? elemType)
    {
        var addr = EmitArrayAddress(elemType);
        Push(new LoadInst(addr));
    }
    private void ImportStoreElem(TypeDesc? elemType)
    {
        var value = Pop();
        var addr = EmitArrayAddress(elemType);

        Emit(new StoreInst(addr, value));
    }
    private void ImportLoadElemAddr(TypeDesc elemType)
    {
        PushNoEmit(EmitArrayAddress(elemType));
    }
    private AddressInst EmitArrayAddress(TypeDesc? elemType)
    {
        var index = Pop();
        var array = Pop();

        var inst = new ArrayAddrInst(
            array, index, NormalizeStorageType(elemType, array.ResultType),
            inBounds: HasPrefix(InstFlags.NoRangeCheck | InstFlags.NoNullCheck),
            readOnly: HasPrefix(InstFlags.Readonly));
        Emit(inst);
        return inst;
    }

    private void ImportLoadInd(TypeDesc? type)
    {
        var addr = Pop();

        Push(new LoadInst(addr, NormalizeStorageType(type, addr.ResultType), PopPointerFlags()));
    }
    private void ImportStoreInd(TypeDesc? type)
    {
        var value = Pop();
        var addr = Pop();

        Emit(new StoreInst(addr, value, NormalizeStorageType(type, addr.ResultType), PopPointerFlags()));
    }
    private PointerFlags PopPointerFlags()
    {
        var flags = PointerFlags.None;
        if (HasPrefix(InstFlags.Unaligned)) flags |= PointerFlags.Unaligned;
        if (HasPrefix(InstFlags.Volatile)) flags |= PointerFlags.Volatile;
        return flags;
    }
    private static TypeDesc NormalizeStorageType(TypeDesc? type, TypeDesc actualArrayOrPtrType)
    {
        if (type == null) {
            // ld/st .ref variants get null type. Infer from storage type or fallback to `object`.
            type = actualArrayOrPtrType.ElemType ?? PrimType.Object;
        }
        // Primitive type definitions (from CoreLib) and aliases (PrimTypes) can be
        // used interchangeably. The only difference between them is that the
        // definitions provide actual metadata such as methods and interfaces.
        // We'll normalize them to the aliases here for consistency.
        if (type is TypeDef def && PrimType.GetFromDefinition(def) is { } alias) {
            type = alias;
        }
        // Infer unsigned type based on `actualStorageType`.
        // This is useful for load instructions, as they don't have many variants
        // for unsigned types.
        // Once again, the only purpose of this is consistency - signed and unsigned 
        // types can be freely interchanged in both IL and IR.
        var kind = type.Kind;
        var actualKind = actualArrayOrPtrType.ElemType?.Kind ?? TypeKind.Void;

        if (kind != actualKind && actualKind.IsUnsigned() && kind.GetUnsigned() == actualKind) {
            type = PrimType.GetFromKind(kind.GetUnsigned());
        }
        return type;
    }

    private void ImportLoadField(FieldDesc field, bool isStatic)
    {
        var obj = isStatic ? null : Pop();

        if (obj != null && obj.ResultType.IsValueType) {
            if (obj is LoadInst load) {
                obj = load.Address;
            } else {
                Push(new FieldExtractInst(field, obj));
                return;
            }
        }
        var addr = new FieldAddrInst(field, obj);
        Emit(addr);
        Push(new LoadInst(addr, flags: PopPointerFlags()));
    }
    private void ImportStoreField(FieldDesc field, bool isStatic)
    {
        var value = Pop();
        var addr = EmitFieldAddr(field, isStatic);
        Emit(new StoreInst(addr, value, flags: PopPointerFlags()));
    }
    private void ImportFieldAddr(FieldDesc field, bool isStatic)
    {
        PushNoEmit(EmitFieldAddr(field, isStatic));
    }
    private FieldAddrInst EmitFieldAddr(FieldDesc field, bool isStatic)
    {
        var obj = isStatic ? null : Pop();
        
        if (obj is LoadInst ld && ld.ResultType.IsValueType) {
            obj = ld.Address;
        }
        var inst = new FieldAddrInst(field, obj);
        Emit(inst);
        return inst;
    }

    private void ImportCall(MethodDesc method, bool isVirt)
    {
        var args = PopCallArgs(method);
        var constraint = HasPrefix(InstFlags.Constrained) ? _callConstraint : null;
        var inst = new CallInst(method, args, isVirt, constraint);
        Push(inst);
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
        var args = new Value[method.ParamSig.Count - (ctor ? 1 : 0)];
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
        var currRegion = _importer._regionTree!.FindEnclosing(StartOffset);
        var destRegion = _importer._regionTree!.FindEnclosing(targetOffset);
        var targetBlock = _importer.GetBlock(targetOffset).Block;

        currRegion.ExitTargets.Add(targetBlock);

        // Create a chain of blocks leaving all nested regions until target (in reverse order)
        while (currRegion.Parent! != destRegion) {
            currRegion = currRegion.Parent!;

            currRegion.LeavingBlocks ??= new();

            if (currRegion.LeavingBlocks.TryGetValue(targetBlock, out var exitBlock)) {
                targetBlock = exitBlock;
                break;
            }
            var nextBlock = _body.CreateBlock(insertAfter: Block);
            nextBlock.InsertLast(new LeaveInst(targetBlock!));

            currRegion.LeavingBlocks.Add(targetBlock, nextBlock);
            targetBlock = nextBlock;
        }
        TerminateBlock(new LeaveInst(targetBlock), clearStack: true);
    }
    private void ImportResume(bool isFromFilter)
    {
        var filterResult = isFromFilter ? Pop() : null;
        TerminateBlock(new ResumeInst([], filterResult), clearStack: true);
        
        _importer._blocksEndingWithEhResume.Add(this);
    }

    private void ImportThrow(bool isRethrow)
    {
        var exception = isRethrow ? null : Pop();
        TerminateBlock(new ThrowInst(exception), clearStack: true);
    }

    private Exception Error(string? msg = null)
    {
        return new InvalidProgramException(msg);
    }
}

[Flags]
internal enum InstFlags
{
    None            = 0,

    Unaligned       = 1 << 0,
    Volatile        = 1 << 1,
    Tailcall        = 1 << 2,
    Constrained     = 1 << 3,
    Readonly        = 1 << 4,

    // Bits [16..23] are reserved for `no.` prefix
    NoPrefixShift_  = 16,
    NoTypeCheck     = 1 << 16,
    NoRangeCheck    = 1 << 17,
    NoNullCheck     = 1 << 18,
}
[Flags]
internal enum VarFlags
{
    None        = 0,
    // Should not be combined
    IsArg       = 1 << 0,
    IsLocal     = 1 << 1,

    Loaded      = 1 << 2,
    Stored      = 1 << 3,
    AddrTaken   = 1 << 4,
    OpMask = Loaded | Stored | AddrTaken,

    CrossesBlock    = 1 << 5,
    CrossesRegions  = 1 << 6
}