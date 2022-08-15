namespace DistIL.IR.Utils.Parser;

/// <summary> Replace identifiers with node references. </summary>
internal class Binder
{
    readonly Dictionary<string, Node> _ids = new();
    readonly ParserContext _ctx;

    public Binder(ParserContext ctx) => _ctx = ctx;

    public void Process(ProgramNode program)
    {
        //Assign nodes to their identifiers
        foreach (var block in program.Blocks) {
            Assign(block.Label, block);

            foreach (var inst in block.Code) {
                if (inst.ResultVar != null) {
                    Assign(inst.ResultVar, inst);
                }
            }
        }
        //Bind references
        foreach (var block in program.Blocks) {
            foreach (var inst in block.Code) {
                foreach (ref var oper in inst.Operands.AsSpan()) {
                    if (oper is IdNode id) {
                        oper = Lookup(id.Name, inst);
                    }
                }
            }
        }
    }

    private void Assign(string name, Node node)
    {
        if (IsVarName(name)) {
            throw _ctx.Error(node, "Identifiers prefixed with '$' or '#' are reserved for variables and arguments.");
        }
        if (!_ids.TryAdd(name, node)) {
            throw _ctx.Error(node, $"Identifier '{name}' is already assigned to a value");
        }
    }

    private Node Lookup(string name, InstNode parent)
    {
        if (!_ids.TryGetValue(name, out var node)) {
            if (IsVarName(name)) {
                _ids[name] = node = new VarNode(name);
            } else {
                throw _ctx.Error(parent, $"Unknown identifier '{name}'");
            }
        }
        if (node is VarNode vn && parent.Opcode == "ldvar") {
            vn.Type ??= parent.ResultType;
        }
        return node;
    }

    private static bool IsVarName(string name) => name[0] is '$' or '#';
}