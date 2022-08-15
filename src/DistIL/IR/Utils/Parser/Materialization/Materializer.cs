namespace DistIL.IR.Utils.Parser;

/// <summary> Converts the AST into proper IR </summary>
internal partial class Materializer
{
    readonly MethodBody _method;
    readonly SymbolTable _symTable;
    readonly Dictionary<Node, Value> _cache = new();
    readonly TypeResolver _typeResolver;
    readonly ParserContext _ctx;

    public Materializer(ParserContext ctx, MethodBody method)
    {
        _method = method;
        _symTable = method.GetSymbolTable();
        _typeResolver = new TypeResolver();
        _ctx = ctx;
    }

    public void Process(ProgramNode program)
    {
        foreach (var (modName, ns) in program.Imports) {
            var mod = _ctx.ResolveModule(modName);
            _typeResolver.ImportNamespace(mod, ns);
        }

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
        if (node is ConstNode cst) {
            return cst.Value;
        }
        if (_cache.TryGetValue(node, out var value)) {
            return value;
        }
        return _cache[node] = node switch {
            BlockNode => _method.CreateBlock(),
            InstNode c => Materialize(c),
            MethodNode c => Materialize(c),
            FieldNode c => Materialize(c),
            VarNode c => Materialize(c)
        };
    }

    private Instruction Materialize(InstNode node)
    {
        var resultType = ResolveType(node.ResultType);

        var opers = new Value[node.Operands.Count];
        for (int i = 0; i < opers.Length; i++) {
            opers[i] = GetMaterialized(node.Operands[i]);
        }

        var inst = CreateInstUnchecked(node.Opcode, opers, resultType);
        if (inst == null) {
            throw _ctx.Error(node, "Unknown opcode or too few arguments");
        }
        if (inst.ResultType != resultType) {
            throw _ctx.Error(node, "Result type does not match declaration");
        }
        return inst;
    }

    private MethodDesc Materialize(MethodNode node)
    {
        var ownerType = ResolveType(node.Owner);
        var retType = ResolveType(node.RetType);
        var paramTypes = new TypeDesc[node.Params.Count];
        for (int i = 0; i < paramTypes.Length; i++) {
            paramTypes[i] = ResolveType(node.Params[i]);
        }
        return ownerType.FindMethod(node.Name, new MethodSig(retType, paramTypes))
                ?? throw _ctx.Error(node, "Failed to resolve method");
    }

    private FieldDesc Materialize(FieldNode node)
    {
        var ownerType = ResolveType(node.Owner);
        return ownerType.FindField(node.Name)
                ?? throw _ctx.Error(node, "Failed to resolve field");
    }

    private Value Materialize(VarNode node)
    {
        var prefix = node.Name[0];
        var rawName = node.Name.AsSpan(1);

        if (prefix == '#') {
            if (int.TryParse(rawName, out int argIndex)) {
                return _method.Args[argIndex];
            }
            foreach (var arg in _method.Args) {
                if (arg.Name != null && rawName.SequenceEqual(arg.Name)) {
                    return arg;
                }
            }
            throw _ctx.Error(node, "Identifier matches no argument");
        }
        if (prefix == '$') {
            if (node.Type == null) {
                throw _ctx.Error(node, $"Variable must be loaded at least once for its type to be bound.");
            }
            return new Variable(ResolveType(node.Type), false, rawName.ToString());
        }
        throw new InvalidOperationException("VarNode with unknown prefix");
    }

    private TypeDesc ResolveType(TypeNode? node)
    {
        if (node == null) {
            return PrimType.Void;
        }
        return _typeResolver.Resolve(node)
                ?? throw _ctx.Error(node, "Failed to resolve type");
    }
}