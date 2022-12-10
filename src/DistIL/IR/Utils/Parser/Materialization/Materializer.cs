namespace DistIL.IR.Utils.Parser;

/// <summary> Converts the AST into proper IR </summary>
internal partial class Materializer
{
    readonly MethodBody _method;
    readonly SymbolTable _symTable;
    readonly Dictionary<Node, Value> _cache = new(ReferenceEqualityComparer.Instance);
    readonly ParserContext _ctx;

    public Materializer(ParserContext ctx, MethodBody method)
    {
        _method = method;
        _symTable = method.GetSymbolTable();
        _ctx = ctx;
    }

    public void Process(ProgramNode program)
    {
        //Materialize blocks
        foreach (var blockNode in program.Blocks) {
            var block = (BasicBlock)GetMaterialized(blockNode);

            if (blockNode.Label != null) {
                _symTable.SetName(block, blockNode.Label);
            }
            //Materialize and insert instructions
            foreach (var instNode in blockNode.Code) {
                var inst = (Instruction)GetMaterialized(instNode);
                block.InsertLast(inst);

                if (instNode.ResultVar != null) {
                    _symTable.SetName(inst, instNode.ResultVar);
                }
            }
        }
    }

    private Value GetMaterialized(Node node)
    {
        if (node is BoundNode cst) {
            return cst.Value;
        }
        if (_cache.TryGetValue(node, out var value)) {
            return value;
        }
        return _cache[node] = node switch {
            BlockNode => _method.CreateBlock(),
            InstNode c => Materialize(c),
            VarNode c => Materialize(c)
        };
    }

    private Instruction Materialize(InstNode node)
    {
        var resultType = node.ResultType ?? PrimType.Void;
        var opers = new Value[node.Operands.Count];
        for (int i = 0; i < opers.Length; i++) {
            opers[i] = GetMaterialized(node.Operands[i]);
        }

        var inst = CreateInstUnchecked(node.Opcode, opers, resultType);
        if (inst == null) {
            throw _ctx.Error(node, "Unknown opcode or too few arguments");
        }
        if (inst.ResultType != resultType) {
            throw _ctx.Error(node, "Instruction result type does not match declaration");
        }
        return inst;
    }

    private Value Materialize(VarNode node)
    {
        var prefix = node.Name[0];
        var name = node.Name.AsSpan(1);

        if (prefix == '#') {
            if (int.TryParse(name, out int argIndex)) {
                return _method.Args[argIndex];
            }
            foreach (var arg in _method.Args) {
                if (arg.Name != null && name.SequenceEqual(arg.Name)) {
                    return arg;
                }
            }
            throw _ctx.Error(node, "Identifier matches no argument");
        }
        if (prefix == '$') {
            if (node.Type == null) {
                throw _ctx.Error(node, $"Variable must be loaded at least once for its type to be bound.");
            }
            return new Variable(node.Type, name.ToString());
        }
        throw new InvalidOperationException("VarNode with unknown prefix");
    }
}