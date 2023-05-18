namespace DistIL.Analysis;

public class InterferenceGraph : IMethodAnalysis
{
    Dictionary<Instruction, int> _defNodeIds = new();
    Node[] _nodes = new Node[16];

    //The ForestAnalysis may re-order instructions beyond their original live-ranges,
    //so we must take it in consideration when generating the interference graph.
    public InterferenceGraph(MethodBody method, LivenessAnalysis liveness, ForestAnalysis forest)
    {
        var liveNow = new RefSet<Instruction>();

        foreach (var block in method) {
            foreach (var def in liveness.GetLiveOut(block)) {
                if (forest.IsLeaf(def)) {
                    //Some instructions are always rematerialized so we need to assume that its dependencies are live.
                    //In most cases this will not actually extend live ranges because ForestAnalysis uses a few heuristics.
                    MarkTreeUses(def);
                    continue;
                }
                liveNow.Add(def);
            }

            foreach (var inst in block.Reversed()) {
                if (!forest.IsTreeRoot(inst)) continue;

                if (inst.HasResult) {
                    //Definition of `inst` marks the start of its live-range
                    liveNow.Remove(inst);

                    //All currently live defs interfere with `inst`
                    foreach (var otherInst in liveNow) {
                        AddEdge(inst, otherInst);
                    }
                }
                //Use of a def possibly marks the end of its live-range
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
        //Avoid creating edges for defs of different types to make the graph smaller.
        if (!NeedsEdgeForTypes(a.ResultType, b.ResultType)) return;
        
        var na = GetOrCreateNode(a);
        var nb = GetOrCreateNode(b);

        nb.Adjacent.Add(na.Id);
        na.Adjacent.Add(nb.Id);
    }

    public static bool NeedsEdgeForTypes(TypeDesc a, TypeDesc b)
    {
        return a == b;
    }

    public bool HasEdge(Instruction a, Instruction b)
    {
        if (!_defNodeIds.TryGetValue(a, out int ia) || !_defNodeIds.TryGetValue(b, out int ib)) {
            return false;
        }
        var na = GetNode(ia);
        var nb = GetNode(ib);

        Debug.Assert(na.Adjacent.Contains(nb.Id) == nb.Adjacent.Contains(na.Id));
        return na.Adjacent.Contains(nb.Id);
    }

    public bool TryMerge(Instruction a, Instruction b)
    {
        var na = GetOrCreateNode(a);
        var nb = GetOrCreateNode(b);

        bool interferes = na.Adjacent.Contains(nb.Id);
        return !interferes && MergeNodes(na, nb);
    }

    private bool MergeNodes(Node a, Node b)
    {
        if (a != b) {
            //Replace all existing edges to B with A
            foreach (var id in b.Adjacent) {
                var node = GetNode(id);
                node.Adjacent.Remove(b.Id);
                node.Adjacent.Add(a.Id);
            }
            a.Adjacent.Union(b.Adjacent);

            b.MergedWith = a;
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
            return _nodes[id] = new Node(id);
        }
        return GetNode(id);
    }

    private Node GetNode(int id)
    {
        var node = _nodes[id];
        while (node.MergedWith != null) {
            node = node.MergedWith;
        }
        return node;
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
        public ILVariable? Register;

        internal Node? MergedWith;

        public Node(int id) => Id = id;
    }
}