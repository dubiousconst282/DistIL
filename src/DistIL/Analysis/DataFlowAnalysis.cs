namespace DistIL.Analysis;

//https://en.wikipedia.org/wiki/Data-flow_analysis
/// <summary> Generic base for a gen-kill data flow analysis. </summary>
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
        GraphTraversal.DepthFirst(method.EntryBlock, postVisit: (block) => {
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

    protected struct BlockState
    {
        public BitSet In, Out, Killed;
        internal bool InWorklist;
    }

    /// <summary> Bi-directional map of <typeparamref name="T"/> and sequential integer ids. </summary>
    public class Palette<T> where T : notnull
    {
        internal readonly Dictionary<T, int> _ids;

        public Palette(IEqualityComparer<T>? comparer = null)
            => _ids = new(comparer);

        public int Alloc(T value)
        {
            if (!_ids.TryGetValue(value, out int id)) {
                _ids[value] = id = _ids.Count;
            }
            return id;
        }
        public int Get(T value) => _ids.GetValueOrDefault(value, -1);
        //TODO: T Get(int id);
    }
    /// <summary> Ordered set of arbitrary items backed by a <see cref="BitSet"/>, which indexes a <see cref="Palette{T}"/>. </summary>
    public struct IndirectSet<T> where T : notnull
    {
        public readonly Palette<T> Palette;
        public readonly BitSet Entries;

        public IndirectSet(Palette<T> palette, BitSet entries)
            => (Palette, Entries) = (palette, entries);

        public bool Contains(T value)
            => Entries.Contains(Palette.Get(value)); //BitSet.Contains() allows negative indices

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var (item, id) in Palette._ids) {
                if (!Entries.Contains(id)) continue;

                if (sb.Length > 0) sb.Append(", ");
                sb.Append(item);
            }
            return sb.ToString();
        }
    }
}

public class VarLivenessAnalysis : DataFlowAnalysis
{
    private readonly Palette<Variable> _varIds = new();

    public VarLivenessAnalysis(MethodBody method)
        : base(method, backward: true) { }

    protected override void Initialize(BasicBlock block, out BlockState state)
    {
        var globals = new BitSet();
        var killed = new BitSet();

        foreach (var inst in block) {
            if (inst is VarAccessInst acc) {
                int varId = _varIds.Alloc(acc.Var);

                if (inst is StoreVarInst) {
                    killed.Add(varId);
                } else if (inst is LoadVarInst && !killed.Contains(varId)) {
                    globals.Add(varId); //used before being assigned in this block
                }
            }
        }
        state = new() { Killed = killed, In = globals, Out = new BitSet() };
    }

    public IndirectSet<Variable> GetLiveIn(BasicBlock block) => new(_varIds, GetState(block).In);
    public IndirectSet<Variable> GetLiveOut(BasicBlock block) => new(_varIds, GetState(block).Out);
}