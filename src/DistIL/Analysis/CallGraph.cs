namespace DistIL.Analysis;

/// <summary> A imprecise call graph analysis which works over the raw CIL code. </summary>
public class CallGraph
{
    readonly Dictionary<MethodDef, Node> _nodes = new(1 << 15);

    public int NumMethods => _nodes.Count;

    public CallGraph(ModuleDef module)
    {
        foreach (var method in module.AllMethods()) {
            if (method.ILBody == null) continue;

            var called = default(HashSet<MethodDef>);

            //TODO: deeper analysis for inlining heuristics
            foreach (ref var inst in method.ILBody.Instructions.AsSpan()) {
                if (inst.Operand is MethodDefOrSpec oper) {
                    called ??= new(4);
                    called.Add(oper.Definition);
                }
            }

            _nodes[method] = new() {
                Called = called,
                Id = _nodes.Count
            };
        }
    }

    /// <summary> Performs a depth-first traversal over the call graph. </summary>
    public void Traverse(Action<MethodDef>? preVisit = null, Action<MethodDef>? postVisit = null)
    {
        const byte kUnseen = 0, kVisiting = 1, kDone = 2;
        var state = new byte[_nodes.Count];
        var worklist = new ArrayStack<MethodDef>();

        foreach (var (entryMethod, entryNode) in _nodes) {
            if (state[entryNode.Id] == kDone) continue;

            Debug.Assert(state[entryNode.Id] == kUnseen);
            Push(entryMethod, entryNode.Id);

            while (!worklist.IsEmpty) {
                var method = worklist.Top;
                ref var node = ref _nodes.GetRef(method);

                if (state[node.Id] == kVisiting) {
                    state[node.Id] = kDone;
                    preVisit?.Invoke(method);

                    if (node.Called != null) {
                        foreach (var succ in node.Called) {
                            if (_nodes.TryGetValue(succ, out var succNode)) {
                                Push(succ, succNode.Id);
                            }
                        }
                    }
                } else if (state[node.Id] == kDone) {
                    postVisit?.Invoke(method);
                    worklist.Pop();
                }
            }
        }

        void Push(MethodDef method, int id)
        {
            if (state[id] == kUnseen) {
                state[id] = kVisiting;
                worklist.Push(method);
            }
        }
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        int depth = 0;
        Traverse(
            preVisit: (m) => {
                depth++;
                sb.Append(' ', depth * 2);
                sb.Append(m).Append('\n');
            },
            postVisit: (m) => {
                depth--;
            }
        );
        return sb.ToString();
    }

    private struct Node
    {
        public HashSet<MethodDef>? Called;
        public int Id;
    }
}