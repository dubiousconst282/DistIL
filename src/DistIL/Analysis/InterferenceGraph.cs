namespace DistIL.Analysis;

public class InterferenceGraph : IMethodAnalysis
{
    Dictionary<Instruction, int> _defNodeIds = new();
    Node[] _nodes = new Node[16];

    // The ForestAnalysis may re-order instructions beyond their original live-ranges,
    // so we must take it in consideration when generating the interference graph.
    public InterferenceGraph(MethodBody method, LivenessAnalysis liveness, ForestAnalysis forest)
    {
        var liveNow = new RefSet<Instruction>();

        foreach (var block in method) {
            foreach (var def in liveness.GetLiveOut(block)) {
                if (forest.IsLeaf(def)) {
                    // Some instructions are always rematerialized so we need to ensure that their dependencies are live.
                    MarkTreeUses(def);
                    continue;
                }
                liveNow.Add(def);
            }

            foreach (var inst in block.Reversed()) {
                if (!forest.IsTreeRoot(inst)) continue;

                if (inst.HasResult) {
                    // Definition of `inst` marks the start of its live-range
                    liveNow.Remove(inst);

                    // All currently live defs interfere with `inst`
                    foreach (var otherInst in liveNow) {
                        AddEdge(inst, otherInst);
                    }
                }
                // Use of a def possibly marks the end of its live-range
                MarkTreeUses(inst);
            }
            liveNow.Clear();

            void MarkTreeUses(Instruction inst)
            {
                foreach (var oper in inst.Operands) {
                    if (oper is not Instruction operI) continue;

                    liveNow.Add(operI);

                    if (forest.IsLeaf(operI)) {
                        MarkTreeUses(operI);
                    }
                }
            }
        }
    }

    static IMethodAnalysis IMethodAnalysis.Create(IMethodAnalysisManager mgr)
        => new InterferenceGraph(mgr.Method, mgr.GetAnalysis<LivenessAnalysis>(), mgr.GetAnalysis<ForestAnalysis>());

    public void AddEdge(Instruction a, Instruction b)
    {
        // Avoid creating edges for defs of different types to make the graph smaller.
        if (a.ResultType != b.ResultType) return;
        
        var na = GetOrCreateNode(a);
        var nb = GetOrCreateNode(b);

        nb.Adjacent.Add(na.Id);
        na.Adjacent.Add(nb.Id);
    }

    public bool HasEdge(Instruction a, Instruction b)
    {
        var na = GetNode(a);
        var nb = GetNode(b);

        if (na == null || nb == null) {
            return false;
        }
        Debug.Assert(na.Adjacent.Contains(nb.Id) == nb.Adjacent.Contains(na.Id));
        return na.Adjacent.Contains(nb.Id);
    }

    /// <summary> Attempts to merge node <paramref name="b"/> into <paramref name="a"/> if they don't interfere. </summary>
    public bool TryMerge(Instruction a, Instruction b)
    {
        var na = GetOrCreateNode(a);
        var nb = GetOrCreateNode(b);

        if (na.Adjacent.Contains(nb.Id)) {
            return false; // interference
        }

        if (na != nb) {
            // Replace all existing edges to B with A
            foreach (int id in nb.Adjacent) {
                var node = GetNode(id);
                node.Adjacent.Remove(nb.Id);
                node.Adjacent.Add(na.Id);
            }
            na.Adjacent.Union(nb.Adjacent);

            _nodes[nb.Id] = na;
            _defNodeIds[b] = na.Id;
        }
        return true;
    }

    public Node? GetNode(Instruction def)
    {
        return _defNodeIds.TryGetValue(def, out int id) ? GetNode(id) : null;
    }

    public Node GetOrCreateNode(Instruction def)
    {
        ref int id = ref _defNodeIds.GetOrAddRef(def, out bool exists);
        if (!exists) {
            id = _defNodeIds.Count - 1;
            
            if (id >= _nodes.Length) {
                Array.Resize(ref _nodes, _nodes.Length * 2);
            }
            return _nodes[id] = new Node(id, def.ResultType);
        }
        return GetNode(id);
    }

    private Node GetNode(int id)
    {
        return _nodes[id];
    }

    public IEnumerable<(Instruction K, Node V)> GetNodes()
        => _defNodeIds.Select(e => (e.Key, GetNode(e.Value)));

    public IEnumerable<Node> GetAdjacent(Node node)
        => node.Adjacent.GetEnumerator().AsEnumerable().Select(GetNode);

    public override string ToString()
    {
        var sw = new StringWriter();
        var pc = new PrintContext(sw, _defNodeIds.First().Key.GetSymbolTable()!);

        sw.Write("graph {\n");
        sw.Write("  node[shape=\"box\"]\n");

        var edges = new HashSet<(int, int)>();

        foreach (var (_, nodeA) in GetNodes()) {
            foreach (var nodeB in GetAdjacent(nodeA)) {
                var key = (A: nodeA.Id, B: nodeB.Id);

                if (key.A > key.B) {
                    key = (key.B, key.A);
                }
                if (edges.Add(key)) {
                    pc.Print($"  n{key.A} -- n{key.B}\n");
                }
            }
        }

        foreach (var group in _defNodeIds.GroupBy(e => GetNode(e.Value), e => e.Key)) {
            sw.Write($"n{group.Key.Id}[label=<");
            sw.Write("<font color=\"blue\" point-size=\"10\">");
            pc.Print(group.First().ResultType);
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
        public readonly BitSet Adjacent = new();
        public readonly int Id;
        public int Color;
        public TypeDesc RegisterType;

        public Node(int id, TypeDesc regType) { Id = id; RegisterType = regType; }
    }
}