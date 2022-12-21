namespace DistIL.Analysis;

public class InterferenceGraph : IMethodAnalysis
{
    readonly Dictionary<Instruction, Node> _nodes = new();

    public InterferenceGraph(MethodBody method, LivenessAnalysis liveness)
    {
        var liveNow = new RefSet<Instruction>();

        foreach (var block in method) {
            foreach (var def in liveness.GetLiveOut(block)) {
                liveNow.Add(def);
            }

            foreach (var inst in block.Reversed()) {
                if (inst.HasResult) {
                    //Definition of `inst` marks the start of its live-range
                    liveNow.Remove(inst);

                    //All currently live defs interfere with `inst`
                    foreach (var otherInst in liveNow) {
                        AddEdge(inst, otherInst);
                    }
                }
                //Use of a def possibly marks the end of its live-range
                foreach (var oper in inst.Operands) {
                    if (oper is Instruction operI) {
                        liveNow.Add(operI);
                    }
                }
            }
            liveNow.Clear();
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new InterferenceGraph(mgr.Method, mgr.GetAnalysis<LivenessAnalysis>(preserve: true));

    public void AddEdge(Instruction a, Instruction b)
    {
        var na = GetOrCreateNode(a);
        var nb = GetOrCreateNode(b);

        na.Adjacent.Add(nb);
        nb.Adjacent.Add(na);
    }

    public bool HasEdge(Instruction a, Instruction b)
    {
        return _nodes.TryGetValue(a, out var na) &&
               _nodes.TryGetValue(b, out var nb) &&
                na.Adjacent.Contains(nb);
    }

    public bool TryMerge(Instruction a, Instruction b)
    {
        if (!_nodes.TryGetValue(a, out var na) ||
            !_nodes.TryGetValue(b, out var nb) ||
            na.Adjacent.Contains(nb)
        ) {
            return false;
        }

        foreach (var node in nb.Adjacent) {
            node.Adjacent.Remove(nb);
            node.Adjacent.Add(na);
            na.Adjacent.Add(node);
        }
        _nodes[b] = na;

        foreach (var kak in _nodes) {
            if (kak.Value == nb) {
                _nodes[kak.Key] = na;
            }
        }
        return true;
    }

    public Node? GetNode(Instruction def)
        => _nodes.GetValueOrDefault(def);

    public Node GetOrCreateNode(Instruction def)
        => _nodes.GetOrAddRef(def)
            ??= new Node() { Index = _nodes.Count - 1 };

    public IReadOnlyDictionary<Instruction, Node> GetNodes()
        => _nodes;

    public override string ToString()
    {
        var sw = new StringWriter();
        var pc = new PrintContext(sw, _nodes.First().Key.GetSymbolTable()!);

        sw.Write("graph {\n");
        sw.Write("  node[shape=\"box\"]\n");

        var edges = new HashSet<(int A, int B)>();
        
        foreach (var nodeA in _nodes.Values) {
            foreach (var nodeB in nodeA.Adjacent) {
                int idxA = nodeA.Index;
                int idxB = nodeB.Index;
                var key = idxA > idxB ? (idxB, idxA) : (idxA, idxB);

                if (edges.Add(key)) {
                    pc.Print($"  n{idxA} -- n{idxB}\n");
                }
            }
        }

        foreach (var group in _nodes.GroupBy(e => e.Value, e => e.Key)) {
            sw.Write($"n{group.Key.Index}[label=<");
            sw.Write("<font color=\"blue\" point-size=\"10\">");
            sw.Write(group.First().ResultType);
            sw.Write(" </font>");

            pc.Print($"{group: $}");
            if (group.Key.Color != 0) {
                sw.Write("<sup><font color=\"red\" point-size=\"10\">");
                sw.Write(group.Key.Color);
                sw.Write("</font></sup>");
            }
            sw.Write(">]\n");
        }
        sw.Write("}");
        return sw.ToString();
    }

    public class Node
    {
        public HashSet<Node> Adjacent = new();
        public int Index;
        public int Color;
        public Variable? Register;
    }
}