namespace DistIL.IR.Utils;

public class IRCloner
{
    //Mapping from old to new (clonned) values
    readonly Dictionary<Value, Value> _mappings = new();
    //Values that must be remapped and replaced last (they depend on defs in an unprocessed block).
    readonly RefSet<TrackedValue> _pendingValues = new();
    readonly InstCloner _instCloner;
    readonly List<BasicBlock> _srcBlocks = new();

    public IRCloner()
    {
        _instCloner = new(this);
    }

    public void AddMapping(Value oldVal, Value newVal)
    {
        _mappings.Add(oldVal, newVal);
    }
    /// <summary> Schedules the cloning of the specified block. </summary>
    public void AddBlock(BasicBlock srcBlock, BasicBlock destBlock)
    {
        Ensure.That(destBlock.First == null, "Destination block must be empty");
        _mappings.Add(srcBlock, destBlock);
        _srcBlocks.Add(srcBlock);
    }

    /// <summary> Clones all scheduled blocks. </summary>
    public void Run()
    {
        foreach (var block in _srcBlocks) {
            var destBlock = (BasicBlock)_mappings[block];

            //Clone instructions
            foreach (var inst in block) {
                var newVal = _instCloner.Clone(inst);
                //Clone() may fold constants: `add r10, 0` -> `r10`,
                //so we can only insert a inst if it isn't already in a block.
                if (newVal is Instruction { Block: null } newInst) {
                    destBlock.InsertLast(newInst);
                }
                if (newVal.HasResult) {
                    _mappings.Add(inst, newVal);
                }
            }
        }
        //Remap pending values
        foreach (var value in _pendingValues) {
            var newValue = Remap(value) ??
                throw new InvalidOperationException("No mapping for value " + value);
            
            foreach (var (user, operIdx) in value.Uses()) {
                //If we don't have a mapping for the block `user` is in, assume it's
                //a newly cloned instruction and proceed replacing its operand
                if (!_mappings.ContainsKey(user.Block)) {
                    user.ReplaceOperand(operIdx, newValue);
                }
            }
        }
    }

    private Value? Remap(Value value)
    {
        if (_mappings.TryGetValue(value, out var newValue)) {
            return newValue;
        }
        if (value is Variable var) {
            newValue = new Variable(var.Type, var.IsPinned);
            _mappings.Add(value, newValue);
            return newValue;
        }
        if (value is Const or EntityDesc or Undef) {
            return value;
        }
        //At this point, all non TrackedValue`s, must have been handled
        _pendingValues.Add((TrackedValue)value); 
        return null;
    }

    class InstCloner : InstVisitor
    {
        readonly IRCloner _ctx;
        Value _result = null!;

        public InstCloner(IRCloner ctx) => _ctx = ctx;

        public Value Clone(Instruction inst)
        {
            inst.Accept(this);
            return _result;
        }

        private void Out(Value val) => _result = val;

        private Value Remap(Value val) => _ctx.Remap(val) ?? val;
        private Variable Remap(Variable var) => (Variable)Remap((Value)var);
        private BasicBlock Remap(BasicBlock block) => (BasicBlock)Remap((Value)block);

        private Value[] RemapArgs(ReadOnlySpan<Value> args)
        {
            var newArgs = new Value[args.Length];
            for (int i = 0; i < args.Length; i++) {
                newArgs[i] = Remap(args[i]);
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

        public void Visit(LoadVarInst inst) => Out(new LoadVarInst(Remap(inst.Var)));
        public void Visit(StoreVarInst inst) => Out(new StoreVarInst(Remap(inst.Var), Remap(inst.Value)));
        public void Visit(VarAddrInst inst) => Out(new VarAddrInst(Remap(inst.Var)));

        public void Visit(LoadPtrInst inst) => Out(new LoadPtrInst(Remap(inst.Address), inst.ElemType, inst.Flags));
        public void Visit(StorePtrInst inst) => Out(new StorePtrInst(Remap(inst.Address), Remap(inst.Value), inst.ElemType, inst.Flags));

        public void Visit(ArrayLenInst inst) => Out(new ArrayLenInst(Remap(inst.Array)));
        public void Visit(LoadArrayInst inst) => Out(new LoadArrayInst(Remap(inst.Array), Remap(inst.Index), inst.ElemType, inst.Flags));
        public void Visit(StoreArrayInst inst) => Out(new StoreArrayInst(Remap(inst.Array), Remap(inst.Index), Remap(inst.Value), inst.ElemType, inst.Flags));
        public void Visit(ArrayAddrInst inst) => Out(new ArrayAddrInst(Remap(inst.Array), Remap(inst.Index), inst.ElemType, inst.Flags));

        public void Visit(LoadFieldInst inst) => Out(new LoadFieldInst(inst.Field, inst.IsStatic ? null : Remap(inst.Obj)));
        public void Visit(StoreFieldInst inst) => Out(new StoreFieldInst(inst.Field, inst.IsStatic ? null : Remap(inst.Obj), Remap(inst.Value)));
        public void Visit(FieldAddrInst inst) => Out(new FieldAddrInst(inst.Field, inst.IsStatic ? null : Remap(inst.Obj)));

        public void Visit(CallInst inst) => Out(new CallInst(inst.Method, RemapArgs(inst.Args), inst.IsVirtual, inst.Constraint));
        public void Visit(NewObjInst inst) => Out(new NewObjInst(inst.Constructor, RemapArgs(inst.Args)));
        public void Visit(FuncAddrInst inst) => Out(new FuncAddrInst(inst.Method, inst.IsVirtual ? Remap(inst.Object) : null));
        public void Visit(IntrinsicInst inst) => Out(new IntrinsicInst(inst.Id, inst.ResultType, RemapArgs(inst.Args)));

        public void Visit(ReturnInst inst) => Out(new ReturnInst(inst.HasValue ? Remap(inst.Value) : null));
        public void Visit(BranchInst inst) => Out(inst.IsJump ? new BranchInst(Remap(inst.Then)) : new BranchInst(Remap(inst.Cond), Remap(inst.Then), Remap(inst.Else)));
        public void Visit(SwitchInst inst) => Out(new SwitchInst(RemapArgs(inst.Operands), inst.TargetMappings.AsSpan().ToArray()));
        public void Visit(PhiInst inst) => Out(new PhiInst(inst.ResultType, RemapArgs(inst.Operands)));

        public void Visit(GuardInst inst) => Out(new GuardInst(inst.Kind, Remap(inst.HandlerBlock), inst.CatchType, inst.HasFilter ? Remap(inst.FilterBlock) : null));
        public void Visit(ThrowInst inst) => Out(new ThrowInst(inst.IsRethrow ? null : Remap(inst.Exception)));
        public void Visit(LeaveInst inst) => Out(new LeaveInst(Remap(inst.Target)));
        public void Visit(ContinueInst inst) => Out(new ContinueInst(inst.IsFromFilter ? Remap(inst.FilterResult) : null));
    }
}