namespace DistIL.IR.Utils;

using DistIL.Passes;

public class Cloner
{
    readonly MethodBody _targetMethod;
    readonly Dictionary<Value, Value> _mappings = new(); //mapping from old to new (clonned) values
    readonly RefSet<Value> _valuesPendingRemap = new(); //values needing remap bc they weren't at first
    readonly InstCloner _instCloner;

    public Cloner(MethodBody targetMethod)
    {
        _targetMethod = targetMethod;
        _instCloner = new(this);
    }

    public void AddMapping(Value key, Value val)
    {
        _mappings.Add(key, val);
    }

    //TODO: Streaming API
    public List<BasicBlock> CloneBlocks(MethodBody method)
    {
        var newBlocks = new List<BasicBlock>();
        //List of instructions that need to be remapped last (they may depend on a instruction in a unvisited pred block)
        var pendingInsts = new List<Instruction>();

        //Create empty blocks to initialize mappings
        foreach (var oldBlock in method) {
            var newBlock = _targetMethod.CreateBlock();
            _mappings.Add(oldBlock, newBlock);
            newBlocks.Add(newBlock);
        }
        //Fill in the new blocks
        int blockIdx = 0;
        foreach (var oldBlock in method) {
            var newBlock = newBlocks[blockIdx++];
            //Clone edges
            foreach (var succ in oldBlock.Succs) {
                newBlock.Succs.Add(Remap(succ));
            }
            foreach (var pred in oldBlock.Preds) {
                newBlock.Preds.Add(Remap(pred));
            }
            //Clone instructions
            foreach (var inst in oldBlock) {
                var (newVal, fullyRemapped) = _instCloner.Clone(inst);
                //Clone() may fold constants: "add r10, 0" -> "r10",
                //so we can only insert if isn't already in a block.
                if (newVal is Instruction newInst && newInst.Block == null) {
                    newBlock.InsertLast(newInst);
                    if (!fullyRemapped) {
                        pendingInsts.Add(newInst);
                    }
                }
                if (newVal.HasResult) {
                    _mappings.Add(inst, newVal);
                }
            }
        }
        //Remap pending instructions
        foreach (var inst in pendingInsts) {
            var opers = inst.Operands;
            for (int i = 0; i < opers.Length; i++) {
                if (!_valuesPendingRemap.Contains(opers[i])) continue;
                if (!Remap(opers[i], out var newValue)) {
                    throw new InvalidOperationException("No mapping for value " + opers[i]);
                }
                inst.ReplaceOperand(i, newValue);
            }
        }
        return newBlocks;
    }

    private bool Remap(Value value, [NotNullWhen(true)] out Value? newValue)
    {
        if (_mappings.TryGetValue(value, out newValue)) {
            return true;
        }
        if (value is Variable var) {
            newValue = new Variable(var.Type, var.IsPinned);
            _mappings.Add(value, newValue);
            return true;
        }
        if (value is Const or EntityDesc) {
            newValue = value;
            return true;
        }
        _valuesPendingRemap.Add(value);
        return false;
    }
    private BasicBlock Remap(BasicBlock block)
    {
        if (Remap(block, out var newBlock)) {
            return (BasicBlock)newBlock!;
        }
        throw new InvalidOperationException("No mapping for " + block);
    }

    class InstCloner : InstVisitor
    {
        readonly Cloner _ctx;
        Value _newValue = null!;
        bool _fullyRemapped;

        public InstCloner(Cloner ctx) => _ctx = ctx;

        public (Value NewValue, bool FullyRemapped) Clone(Instruction inst)
        {
            _fullyRemapped = true;
            inst.Accept(this);
            return (_newValue, _fullyRemapped);
        }

        private void Out(Value val)
        {
            _newValue = val;
        }
        private Value Remap(Value val)
        {
            _fullyRemapped &= _ctx.Remap(val, out var newVal);
            return newVal ?? val;
        }
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
            Out(ConstFold.FoldBinary(inst.Op, left, right)
                ?? new BinaryInst(inst.Op, left, right));
        }
        public void Visit(UnaryInst inst)
        {
            var value = Remap(inst.Value);
            Out(ConstFold.FoldUnary(inst.Op, value)
                ?? new UnaryInst(inst.Op, value));
        }
        public void Visit(CompareInst inst)
        {
            var left = Remap(inst.Left);
            var right = Remap(inst.Right);
            Out(ConstFold.FoldCompare(inst.Op, left, right)
                ?? new CompareInst(inst.Op, left, right));
        }
        public void Visit(ConvertInst inst)
        {
            var value = Remap(inst.Value);
            Out(ConstFold.FoldConvert(value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned)
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
        public void Visit(SwitchInst inst) => Out(new SwitchInst(RemapArgs(inst.Operands)));
        public void Visit(PhiInst inst) => Out(new PhiInst(inst.ResultType, RemapArgs(inst.Operands)));

        public void Visit(GuardInst inst) => Out(new GuardInst(inst.Kind, Remap(inst.HandlerBlock), inst.CatchType, inst.HasFilter ? Remap(inst.FilterBlock) : null));
        public void Visit(ThrowInst inst) => Out(new ThrowInst(inst.IsRethrow ? null : Remap(inst.Exception)));
        public void Visit(LeaveInst inst) => Out(new LeaveInst((GuardInst)Remap(inst.ParentGuard), Remap(inst.Target)));
        public void Visit(ContinueInst inst) => Out(inst.IsFromFilter
            ? new ContinueInst((GuardInst)Remap(inst.ParentGuard), inst.FilterResult)
            : new ContinueInst((GuardInst)Remap(inst.ParentGuard)));

        public void VisitDefault(Instruction inst) => Ensure(false, "Missing cloner for " + inst.GetType());
    }
}