namespace DistIL.Analysis;

using DistIL.IR;

public class LivenessAnalysis : IMethodAnalysis
{
    readonly Dictionary<Variable, int> _ids = new();
    readonly Dictionary<BasicBlock, (BitSet LiveOutVars, BitSet GlobalVars, BitSet KilledVars)> _blockInfos;

    public LivenessAnalysis(MethodBody method)
    {
        _blockInfos = new(method.NumBlocks);

        //Collect initial information
        //By visiting blocks in post order, well get most predecessors first, so the other loop should converge quicker.
        //(The BCL Dictionary preserves insertion order as long as we don't remove entries)
        GraphTraversal.DepthFirst(method.EntryBlock, postVisit: block => {
            //Global variables (aka "upward-exposed") is the set of variables that are used
            //before any assignment in the block, and killed variables are variables which 
            //were reassigned in the block.
            var globalVars = new BitSet();
            var killedVars = new BitSet();

            foreach (var inst in block) {
                if (inst is VarAccessInst acc) {
                    int id = GetId(acc.Var);

                    if (inst is StoreVarInst store) {
                        killedVars.Add(id);
                    } else if (!killedVars.Contains(id)) {
                        globalVars.Add(id);
                    }
                }
            }
            _blockInfos.Add(block, (new BitSet(), globalVars, killedVars));
        });

        //Compute the dataflow equation until we reach a fixed point
        //  LiveOut[b] = ∪(s of b.Succs: Globals[s] ∪ (LiveOut[s] ∩ Killed[s]'))
        bool changed = true;
        while (changed) {
            changed = false;

            foreach (var (block, (liveOut, _, _)) in _blockInfos) {
                foreach (var succ in block.Succs) {
                    var (succLiveOut, succGlobals, succKilled) = _blockInfos[succ];

                    changed |= liveOut.Union(succGlobals);
                    changed |= liveOut.UnionDiffs(succLiveOut, succKilled);
                }
            }
        }

        int GetId(Variable var)
        {
            ref int id = ref _ids.GetOrAddRef(var, out bool exists);
            if (!exists) id = _ids.Count - 1;
            return id;
        }
    }

    public static IMethodAnalysis Create(IMethodAnalysisManager mgr)
    {
        return new LivenessAnalysis(mgr.Method);
    }

    public VarSet GetLiveOut(BasicBlock block)
        => new() { _ids = _ids, _vars = _blockInfos.GetValueOrDefault(block).LiveOutVars };

    public VarSet GetGlobals(BasicBlock block)
        => new() { _ids = _ids, _vars = _blockInfos.GetValueOrDefault(block).GlobalVars };

    public VarSet GetKills(BasicBlock block)
        => new() { _ids = _ids, _vars = _blockInfos.GetValueOrDefault(block).KilledVars };

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var block in _blockInfos.Keys.Reverse()) {
            sb.Append($"{block}:\n");
            sb.Append($"  LiveOut: {GetLiveOut(block)}\n");
            sb.Append($"  Globals: {GetGlobals(block)}\n");
            sb.Append($"  Kills:   {GetKills(block)}\n");
        }
        return sb.ToString();
    }
}

public struct VarSet
{
    internal Dictionary<Variable, int> _ids;
    internal BitSet? _vars;

    public readonly bool Contains(Variable var)
        => _vars != null && _ids.TryGetValue(var, out var id) && _vars.Contains(id);

    public override string ToString()
    {
        if (_vars == null) {
            return "[]";
        }
        var sb = new StringBuilder("[");
        foreach (var (var, id) in _ids) {
            if (!_vars.Contains(id)) continue;
            if (sb.Length > 1) sb.Append(", ");
            sb.Append(var);
        }
        sb.Append("]");
        return sb.ToString();
    }
}