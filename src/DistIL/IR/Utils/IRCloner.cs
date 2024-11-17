namespace DistIL.IR.Utils;

public class IRCloner
{
    // Mapping from old to new (clonned) values
    readonly Dictionary<Value, Value> _mappings = new();
    // Values that must be remapped and replaced last (they depend on defs in an unprocessed block).
    readonly RefSet<PendingValue> _pendingValues = new();
    readonly InstCloner _instCloner;
    readonly List<BasicBlock> _blocks = new();
    protected readonly GenericContext _genericContext;
    protected readonly MethodBody _destMethod;

    public IRCloner(MethodBody method, GenericContext genericContext = default)
    {
        _destMethod = method;
        _instCloner = new(this);
        _genericContext = genericContext;
    }

    public void AddMapping(Value oldVal, Value newVal)
    {
        _mappings.Add(oldVal, newVal);

        if (oldVal is BasicBlock oldBlock) {
            Ensure.That(newVal is BasicBlock);
            Debug.Assert(((BasicBlock)newVal).Method == _destMethod);
            
            _blocks.Add(oldBlock);
        }
    }

    public BasicBlock GetMapping(BasicBlock originalBlock) => (BasicBlock)_mappings[originalBlock];
    public Value GetMapping(Value originalValue) => _mappings[originalValue];

    /// <summary> Clones all blocks that are reachable from <paramref name="entry"/>. </summary>
    /// <remarks> Destination blocks must be created and mapped beforehand via <see cref="AddMapping(Value, Value)"/>. </remarks>
    public void Run(BasicBlock entry)
    {
        Ensure.That(_mappings.ContainsKey(entry));

        var worklist = new DiscreteStack<BasicBlock>(entry);

        while (worklist.TryPop(out var block)) {
            var clonedBlock = GetMapping(block);
            
            foreach (var inst in block) {
                var clonedInst = Clone(inst);
                // Clone() may fold constants: `add r10, 0` -> `r10`,
                // so we can only insert a inst if it isn't already in a block.
                if (clonedInst is Instruction { Block: null } newInst) {
                    clonedBlock.InsertLast(newInst);
                }
            }

            // If the cloned branch gets folded, we should only push reachable succs.
            var newSuccs = ConstFolding.FoldBlockBranch(clonedBlock)
                ? [..clonedBlock.Succs]
                : default(HashSet<BasicBlock>);

            foreach (var succ in block.Succs) {
                if (newSuccs != null && !newSuccs.Contains(GetMapping(succ))) continue;

                worklist.Push(succ);
            }
        }

        // Remove unreachable blocks
        foreach (var block in _blocks) {
            var clonedBlock = GetMapping(block);

            if (!worklist.WasPushed(block)) {
                clonedBlock.Remove();
            } else if (clonedBlock.NumPreds != block.NumPreds) {
                RemoveUnreachablePhiPreds(clonedBlock);
            }
        }

        // At this point the only pending values should be from phi uses for
        // unreachable pred blocks. Check that they're no longer used.
        foreach (var pending in _pendingValues) {
            Ensure.That(pending.NumUses == 0);
        }
    }

    private void RemoveUnreachablePhiPreds(BasicBlock block)
    {
        HashSet<BasicBlock> actualPreds = [..block.Preds];

        foreach (var phi in block.Phis()) {
            for (int i = phi.NumArgs - 1; i >= 0; i--) {
                if (actualPreds.Contains(phi.GetBlock(i))) continue;

                phi.RemoveArg(i, removeTrivialPhi: true);
            }
        }
    }

    /// <summary> Clones or folds the given instruction. </summary>
    public Value Clone(Instruction inst)
    {
        var clonedInst = CreateClone(inst);

        if (clonedInst.HasResult) {
            ref var mapping = ref _mappings.GetOrAddRef(inst, out bool exists);

            if (exists) {
                if (mapping is not PendingValue pending) {
                    throw new InvalidOperationException("Cloned instruction with an already existing mapping");
                }
                pending.ReplaceUses(clonedInst);
                _pendingValues.Remove(pending);
            }
            mapping = clonedInst;
        }
        return clonedInst;
    } 

    protected virtual Value CreateClone(Instruction inst)
    {
        return _instCloner.Clone(inst);
    }

    /// <summary> Checks if the given cloned block is unreachable and was removed. </summary>
    public bool IsDead(BasicBlock clonedBlock)
    {
        return clonedBlock.Method == null;
    }

    protected Value Remap(Value value)
    {
        if (_mappings.TryGetValue(value, out var newValue)) {
            return newValue;
        }
        if (value is LocalSlot var) {
            var newType = (TypeDesc)Remap(var.Type);
            newValue = _destMethod.CreateVar(newType, pinned: var.IsPinned, hardExposed: var.IsHardExposed);
            _mappings.Add(value, newValue);
            return newValue;
        }
        if (value is Const or Undef) {
            return value;
        }
        var pending = new PendingValue(value);
        _mappings.Add(value, pending);
        _pendingValues.Add(pending);
        return pending;
    }
    protected EntityDesc Remap(EntityDesc entity)
    {
        if (_genericContext.IsNull) {
            return entity;
        }
        return entity switch {
            TypeDesc c => c.GetSpec(_genericContext),
            MethodDesc c => c.GetSpec(_genericContext),
            FieldDesc c => c.GetSpec(_genericContext)
        };
    }

    class PendingValue : TrackedValue
    {
        public PendingValue(Value actual) => ResultType = actual.ResultType;
        public override void Print(PrintContext ctx) => ctx.Print("**PENDING**");
    }

    sealed class InstCloner : InstVisitor
    {
        readonly IRCloner _ctx;
        Value _result = null!;

        public InstCloner(IRCloner ctx) => _ctx = ctx;

        public Value Clone(Instruction inst)
        {
            inst.Accept(this);
            
            if (_result is Instruction clonedInst) {
                clonedInst.DebugLocation = inst.DebugLocation;
            }
            return _result;
        }

        private void Out(Value val) => _result = val;

        private V Remap<V>(V val) where V : Value
            => (V)(_ctx.Remap(val) ?? val);

        private TypeDesc Remap(TypeDesc val) => (TypeDesc)_ctx.Remap(val);
        private MethodDesc Remap(MethodDesc val) => (MethodDesc)_ctx.Remap(val);
        private FieldDesc Remap(FieldDesc val) => (FieldDesc)_ctx.Remap(val);

        private Value[] RemapArgs(ReadOnlySpan<Value> args)
        {
            var newArgs = new Value[args.Length];
            for (int i = 0; i < args.Length; i++) {
                newArgs[i] = Remap(args[i]);
            }
            return newArgs;
        }
        private EntityDesc[] RemapEntities(ReadOnlySpan<EntityDesc> args)
        {
            var newArgs = new EntityDesc[args.Length];
            for (int i = 0; i < args.Length; i++) {
                newArgs[i] = _ctx.Remap(args[i]);
            }
            return newArgs;
        }

        public void Visit(BinaryInst inst)
        {
            var left = Remap(inst.Left);
            var right = Remap(inst.Right);
            Out(ConstFolding.FoldBinary(inst.Op, left, right)
                ?? new BinaryInst(inst.Op, left, right));
        }
        public void Visit(UnaryInst inst)
        {
            var value = Remap(inst.Value);
            Out(ConstFolding.FoldUnary(inst.Op, value)
                ?? new UnaryInst(inst.Op, value));
        }
        public void Visit(CompareInst inst)
        {
            var left = Remap(inst.Left);
            var right = Remap(inst.Right);
            Out(ConstFolding.FoldCompare(inst.Op, left, right)
                ?? new CompareInst(inst.Op, left, right));
        }
        public void Visit(ConvertInst inst)
        {
            var value = Remap(inst.Value);
            Out(ConstFolding.FoldConvert(value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned)
                ?? new ConvertInst(value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned));
        }

        public void Visit(LoadInst inst) => Out(new LoadInst(Remap(inst.Address), Remap(inst.ElemType), inst.Flags));
        public void Visit(StoreInst inst) => Out(new StoreInst(Remap(inst.Address), Remap(inst.Value), Remap(inst.ElemType), inst.Flags));
        public void Visit(FieldExtractInst inst) => Out(new FieldExtractInst(Remap(inst.Field), Remap(inst.Obj)));
        public void Visit(FieldInsertInst inst) => Out(new FieldInsertInst(Remap(inst.Field), Remap(inst.Obj), Remap(inst.NewValue)));

        public void Visit(ArrayAddrInst inst) => Out(new ArrayAddrInst(Remap(inst.Array), Remap(inst.Index), Remap(inst.ElemType), inst.InBounds, inst.IsReadOnly));
        public void Visit(FieldAddrInst inst) => Out(new FieldAddrInst(Remap(inst.Field), inst.IsStatic ? null : Remap(inst.Obj)));
        public void Visit(PtrOffsetInst inst) => Out(new PtrOffsetInst(Remap(inst.BasePtr), Remap(inst.Index), Remap(inst.ResultType), inst.Stride, 0));

        public void Visit(CallInst inst)
        {
            var method = Remap(inst.Method);
            var args = RemapArgs(inst.Args);
            
            Out(ConstFolding.FoldCall(method, args)
                ?? new CallInst(method, args, inst.IsVirtual, inst.Constraint == null ? null : Remap(inst.Constraint)));
        }
        public void Visit(NewObjInst inst) => Out(new NewObjInst(Remap(inst.Constructor), RemapArgs(inst.Args)));
        public void Visit(FuncAddrInst inst) => Out(new FuncAddrInst(Remap(inst.Method), inst.IsVirtual ? Remap(inst.Object) : null));
        public void Visit(IntrinsicInst inst)
        {
            var cloned = inst.CloneWith(Remap(inst.ResultType), RemapEntities(inst.StaticArgs), RemapArgs(inst.Args));
            var folded = ConstFolding.FoldIntrinsic(cloned);

            if (folded != null) {
                Out(folded);
                cloned.Remove(); // delete uses
            } else {
                Out(cloned);
            }
        }
        public void Visit(SelectInst inst)
        {
            var cond = Remap(inst.Cond);
            var ifTrue = Remap(inst.IfTrue);
            var ifFalse = Remap(inst.IfFalse);

            Out(ConstFolding.FoldSelect(cond, ifTrue, ifFalse)
                ?? new SelectInst(cond, ifTrue, ifFalse, Remap(inst.ResultType)));
        }

        public void Visit(ReturnInst inst) => Out(new ReturnInst(inst.HasValue ? Remap(inst.Value) : null));
        public void Visit(BranchInst inst) => Out(inst.IsJump ? new BranchInst(Remap(inst.Then)) : new BranchInst(Remap(inst.Cond), Remap(inst.Then), Remap(inst.Else)));
        public void Visit(SwitchInst inst) => Out(new SwitchInst(RemapArgs(inst.Operands), inst.TargetMappings.AsSpan().ToArray()));
        public void Visit(PhiInst inst) => Out(new PhiInst(Remap(inst.ResultType), RemapArgs(inst.Operands)));

        public void Visit(GuardInst inst) => Out(new GuardInst(inst.Kind, Remap(inst.HandlerBlock), inst.CatchType == null ? null : Remap(inst.CatchType), inst.HasFilter ? Remap(inst.FilterBlock) : null));
        public void Visit(ThrowInst inst) => Out(new ThrowInst(inst.IsRethrow ? null : Remap(inst.Exception)));
        public void Visit(LeaveInst inst) => Out(new LeaveInst(Remap(inst.Target)));
        public void Visit(ResumeInst inst) => Out(new ResumeInst(0, RemapArgs(inst.Operands)));
    }
}