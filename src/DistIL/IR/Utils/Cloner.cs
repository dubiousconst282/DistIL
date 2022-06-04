namespace DistIL.IR.Utils;

using DistIL.Passes;

public class Cloner
{
    readonly MethodBody _targetMethod;
    readonly Dictionary<Value, Value> _mappings = new(); //mapping from old to new (clonned) values
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
                var (newVal, fullyMapped) = _instCloner.Clone(inst);
                if (newVal is Instruction newInst) {
                    newBlock.InsertLast(newInst);
                    if (!fullyMapped) {
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
            for (int i = 0; i < inst.Operands.Length; i++) {
                if (!Remap(inst.Operands[i], out var newValue)) {
                    throw new InvalidOperationException("No mapping for value " + inst.Operands[i]);
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
        if (value is Const) {
            newValue = value;
            return true;
        }
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
            _fullyRemapped = false;
            inst.Accept(this);
            return (_newValue, _fullyRemapped);
        }

        private void Out(Value val)
        {
            _newValue = val;
        }
        private Value Remap(Value val)
        {
            _fullyRemapped |= _ctx.Remap(val, out var newVal);
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

        public void Visit(LoadVarInst inst) => Out(new LoadVarInst(Remap(inst.Source)));
        public void Visit(StoreVarInst inst) => Out(new StoreVarInst(Remap(inst.Dest), Remap(inst.Value)));
        public void Visit(VarAddrInst inst) => Out(new VarAddrInst(Remap(inst.Source)));

        public void Visit(LoadPtrInst inst) => Out(new LoadPtrInst(Remap(inst.Address), inst.ElemType, inst.Flags));
        public void Visit(StorePtrInst inst) => Out(new StorePtrInst(Remap(inst.Address), Remap(inst.Value), inst.ElemType, inst.Flags));

        public void Visit(ArrayLenInst inst) => Out(new ArrayLenInst(Remap(inst.Array)));
        public void Visit(LoadArrayInst inst) => Out(new LoadArrayInst(Remap(inst.Array), Remap(inst.Index), inst.ElemType, inst.Flags));
        public void Visit(StoreArrayInst inst) => Out(new StoreArrayInst(Remap(inst.Array), Remap(inst.Index), Remap(inst.Value), inst.ElemType, inst.Flags));
        public void Visit(ArrayAddrInst inst) => Out(new ArrayAddrInst(Remap(inst.Array), Remap(inst.Index), inst.ElemType, inst.Flags));

        public void Visit(LoadFieldInst inst) => Out(new LoadFieldInst(inst.Field, inst.IsStatic ? null : Remap(inst.Obj)));
        public void Visit(StoreFieldInst inst) => Out(new StoreFieldInst(inst.Field, inst.IsStatic ? null : Remap(inst.Obj), Remap(inst.Value)));
        public void Visit(FieldAddrInst inst) => Out(new FieldAddrInst(inst.Field, inst.IsStatic ? null : Remap(inst.Obj)));

        public void Visit(CallInst inst) => Out(new CallInst(inst.Method, RemapArgs(inst.Args), inst.IsVirtual));
        public void Visit(NewObjInst inst) => Out(new NewObjInst(inst.Constructor, RemapArgs(inst.Args)));

        public void Visit(ReturnInst inst) => Out(new ReturnInst(inst.HasValue ? Remap(inst.Value) : null));
        public void Visit(BranchInst inst) => Out(inst.IsJump ? new BranchInst(Remap(inst.Then)) : new BranchInst(Remap(inst.Cond), Remap(inst.Then), Remap(inst.Else)));
        public void Visit(SwitchInst inst)
        {
            var newBlocks = new BasicBlock[inst.NumTargets];
            for (int i = 0; i < inst.NumTargets; i++) {
                newBlocks[i] = Remap(inst.GetTarget(i));
            }
            Out(new SwitchInst(Remap(inst.Value), Remap(inst.DefaultTarget), newBlocks));
        }
        public void Visit(PhiInst inst)
        {
            var newArgs = new PhiArg[inst.NumArgs];
            for (int i = 0; i < inst.NumArgs; i++) {
                var (block, value) = inst.GetArg(i);
                newArgs[i] = (Remap(block), Remap(value));
            }
            Out(new PhiInst(newArgs));
        }

        protected void VisitDefault(Instruction inst) => Ensure(false, "Missing cloner for " + inst.GetType());
    }
}