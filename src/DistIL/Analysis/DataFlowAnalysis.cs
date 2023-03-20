namespace DistIL.Analysis;

//https://en.wikipedia.org/wiki/Data-flow_analysis
/// <summary> Generic framework for gen-kill data flow problems. </summary>
public abstract class DataFlowAnalysis
{
    protected readonly Dictionary<BasicBlock, BlockState> _states;

    protected DataFlowAnalysis(MethodBody method, bool backward)
    {
        _states = new(method.NumBlocks);

        var worklist = new ArrayStack<BasicBlock>(method.NumBlocks);

        //The initial propagation order has an influence over how quickly the computation will converge.
        //In forward problems, a RPO traversal will be quicker because most predecessor blocks are filled first.
        //Conversely for backward problems, a PO traversal visits all successors blocks first.
        method.TraverseDepthFirst(postVisit: (block) => {
            ref var state = ref _states.GetOrAddRef(block);
            Initialize(block, out state);

            if (backward) {
                //The worklist is processed in reverse order, this will place `block` at ^head
                worklist.HackyFixedUnshift(block);
            } else {
                worklist.Push(block);
            }
            state.InWorklist = true;
        });
        Debug.Assert(!backward || worklist.Count == method.NumBlocks); //HackUnshift() assumes that the stack will be filled to its capacity

        //Compute the dataflow equation until a fixed point is reached
        while (worklist.TryPop(out var block)) {
            ref var state = ref _states.GetRef(block);
            state.InWorklist = false;

            if (backward) {
                TransferBackward(block, ref state);
            } else {
                TransferForward(block, ref state);
            }
        }

        //Backward gen-kill transfer equation:
        //  Out[b] = ∪(s of b.Succs => In[s])
        //  In[b] = Gen[b] ∪ (Out[b] ∩ Killed[b]')
        void TransferBackward(BasicBlock block, ref BlockState state)
        {
            bool changed = false;

            foreach (var succ in block.Succs) {
                changed |= state.Out.Union(GetState(succ).In);
            }
            if (changed) {
                state.In.UnionDiffs(state.Out, state.Killed);

                foreach (var pred in block.Preds) {
                    Push(pred);
                }
            }
        }
        //Forward gen-kill transfer equation:
        //  In[b] = ∪(s of b.Preds => Out[s])
        //  Out[b] = Gen[b] ∪ (In[b] ∩ Killed[b]')
        void TransferForward(BasicBlock block, ref BlockState state)
        {
            bool changed = false;

            foreach (var pred in block.Preds) {
                changed |= state.In.Union(GetState(pred).Out);
            }
            if (changed) {
                state.Out.UnionDiffs(state.In, state.Killed);

                foreach (var succ in block.Succs) {
                    Push(succ);
                }
            }
        }
        void Push(BasicBlock block)
        {
            ref var state = ref _states.GetRef(block);

            if (!state.InWorklist) {
                state.InWorklist = true;
                worklist.Push(block);
            }
        }
    }

    protected abstract void Initialize(BasicBlock block, out BlockState state);

    protected ref BlockState GetState(BasicBlock block) => ref _states.GetRef(block);

    protected virtual void PrintValue(PrintContext ctx, int index) => ctx.Print("#" + index);

    public override string ToString()
    {
        var method = _states.Keys.First().Method;
        var sw = new StringWriter();
        var pc = new PrintContext(sw, method.GetSymbolTable()!);

        foreach (var block in method) {
            ref var state = ref GetState(block);
            if (state.In.PopCount() == 0 && state.Out.PopCount() == 0) continue;

            pc.PrintAsOperand(block);
            Print("  ↑ ", state.In);
            Print("  ↓ ", state.Out);
            pc.Print("\n");
        }
        return sw.ToString();

        void Print(string prefix, BitSet entries)
        {
            int i = 0;
            foreach (int index in entries) {
                pc.Print(i++ > 0 ? ", " : prefix);
                PrintValue(pc, index);
            }
        }
    }

    protected struct BlockState
    {
        public BitSet In, Out, Killed;
        internal bool InWorklist;
    }
}

public class VarLivenessAnalysis : DataFlowAnalysis, IMethodAnalysis
{
    private readonly IndexMap<LocalSlot> _varIds = new();

    public VarLivenessAnalysis(MethodBody method)
        : base(method, backward: true) { }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new VarLivenessAnalysis(mgr.Method);

    protected override void Initialize(BasicBlock block, out BlockState state)
    {
        var globals = new BitSet();
        var killed = new BitSet();

        foreach (var inst in block) {
            if (inst is MemoryInst { Address: LocalSlot slot } acc) {
                int varId = _varIds.Add(slot);

                if (inst is StoreInst) {
                    killed.Add(varId);
                } else if (!killed.Contains(varId)) {
                    Debug.Assert(inst is LoadInst);
                    globals.Add(varId); //used before being assigned in this block
                }
            }
        }
        state = new() { Killed = killed, In = globals, Out = new BitSet() };
    }
    protected override void PrintValue(PrintContext ctx, int index) => ctx.PrintAsOperand(_varIds.At(index));

    public JointBitSet<LocalSlot> GetLiveIn(BasicBlock block) => new(_varIds, GetState(block).In);
    public JointBitSet<LocalSlot> GetLiveOut(BasicBlock block) => new(_varIds, GetState(block).Out);
}