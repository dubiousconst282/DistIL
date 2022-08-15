namespace DistIL.IR.Utils.Parser;

//Program: "Import... Block..."
//Import: " 'import' Id 'from' Id" 
//Block: "Id: Indent "Inst, ..." Dedent" | Inst
//Type: "Id"                -> Basic
//    | "Type[]"            -> Array
//    | "Type*"             -> Pointer
//    | "Type&"             -> Byref
//    | "Type+Id"           -> Nested
//    | "Id`2[Type,...]"    -> Spec
//    | "!0"                -> GenParam
//    | "!!0"               -> GenParam (Method)
//Inst: "Type Id = InstBody"
//    | "Type Id = phi [Label -> Value], ..."
//    | "InstBody"
//InstBody: "Value, ..."
//        | "goto Label"
//        | "goto Value ? Label : Label"
//        | "call|callvirt|newobj Method(this|Type: Value, ...)"
//        | "ldfld|stfld|fldaddr Type::Id [, Value...]"
//Method: "Type::Id"
//      | "Type::Id<Type, ...>"
//Value: Id | Number | String | 'null'

/// <summary> Generates an AST from an arbitrary string. </summary>
internal class AstParser
{
    readonly Lexer _lexer;
    readonly ParserContext _ctx;

    public AstParser(ParserContext ctx)
    {
        _lexer = new Lexer(ctx);
        _ctx = ctx;
    }

    public ProgramNode ParseProgram()
    {
        var imports = new List<(string Mod, string Ns)>();
        var blocks = new List<BlockNode>();

        while (_lexer.MatchKeyword("import")) {
            string ns = _lexer.ExpectId();
            _lexer.ExpectId("from");
            var mod = _lexer.ExpectId();

            imports.Add((mod, ns));
        }
        while (!_lexer.Match(TokenType.EOF)) {
            blocks.Add(ParseBlock());
        }
        return new ProgramNode(imports, blocks);
    }

    public BlockNode ParseBlock()
    {
        //Id: Indent Inst... Dedent | Inst
        var label = _lexer.ExpectId();
        var code = new List<InstNode>();

        _lexer.Expect(TokenType.Colon);

        if (_lexer.Match(TokenType.Indent)) {
            while (!_lexer.Match(TokenType.Dedent) && !_lexer.Match(TokenType.EOF)) {
                code.Add(ParseInst());
            }
        } else {
            code.Add(ParseInst());
        }
        return new BlockNode(label, code);
    }

    public InstNode ParseInst()
    {
        var slot = MatchSlot();
        var opcode = _lexer.ExpectId();
        var opers = new List<Node>();

        switch (opcode) {
            case "phi": {
                //[Id -> Value], ...
                do {
                    _lexer.Expect(TokenType.LBracket);
                    opers.Add(ParseId());
                    _lexer.Expect(TokenType.Arrow);
                    opers.Add(ParseValue());
                    _lexer.Expect(TokenType.RBracket);
                } while (_lexer.Match(TokenType.Comma));
                break;
            }
            case "goto": {
                //goto T  |  goto V ? T : F
                var oper1 = ParseValue();
                opers.Add(oper1);
                if (_lexer.Match(TokenType.QuestionMark)) {
                    opers.Add(ParseId());
                    _lexer.Expect(TokenType.Colon);
                    opers.Add(ParseId());
                }
                break;
            }
            case "call" or "callvirt" or "newobj": {
                //call Type::Id<Type, ...>(this|Type: Value, ...)
                var method = ParseMethodLhs(slot.Type);
                opers.Add(method);

                while (!_lexer.Match(TokenType.RParen)) {
                    method.Params.Add(_lexer.MatchKeyword("this") ? method.Owner : ParseType());
                    _lexer.Expect(TokenType.Colon);
                    opers.Add(ParseValue());
                }
                break;
            }
            case "ldfld" or "stfld" or "fldaddr": {
                opers.Add(ParseField());
                if (_lexer.Match(TokenType.Comma)) {
                    goto default; //cursed or ok?
                }
                break;
            }
            default: {
                //Opcode Value [, ...]  |  Opcode\n
                if (!_lexer.IsNextOnNewLine()) {
                    do {
                        opers.Add(ParseValue());
                    } while (_lexer.Match(TokenType.Comma));
                }
                break;
            }
        }
        return new InstNode(opcode, opers, slot.Type, slot.Name);
    }

    private (TypeNode? Type, string? Name) MatchSlot()
    {
        var startPos = _lexer.Cursor;
        if (
            MatchType() is TypeNode type &&
            _lexer.MatchId() is string name &&
            _lexer.Match(TokenType.Equal)
        ) {
            return (type, name);
        }
        _lexer.Cursor = startPos;
        return default;
    }

    private IdNode ParseId()
    {
        return new IdNode(_lexer.ExpectId());
    }

    private Node ParseValue()
    {
        var token = _lexer.Next();

        return token.Type switch {
            TokenType.Identifier when token.StrValue is "null" =>
                new ConstNode(ConstNull.Create()),
            TokenType.Identifier => new IdNode(token.StrValue),
            TokenType.Number => new ConstNode((Const)token.Value!),
            TokenType.String => new ConstNode(ConstString.Create(token.StrValue)),
            _ => throw _lexer.Error("Value expected")
        };
    }

    //Parse a method left hand side: `OwnerType::Name ['<' Type... '>'] '('`
    private MethodNode ParseMethodLhs(TypeNode? retType)
    {
        var ownerType = ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        var name = _lexer.ExpectId();
        var genPars = default(List<TypeNode>);

        if (_lexer.Match(TokenType.LChevron)) {
            genPars = new();
            while (!_lexer.Match(TokenType.RChevron)) {
                genPars.Add(ParseType());
            }
        }
        _lexer.Expect(TokenType.LParen);

        retType ??= new BasicTypeNode("void");
        var pars = new List<TypeNode>();
        return new MethodNode(ownerType, name, genPars, retType, pars);
    }

    private FieldNode ParseField()
    {
        var ownerType = ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        var name = _lexer.ExpectId();
        return new FieldNode(ownerType, name);
    }

    public TypeNode ParseType()
    {
        return MatchType() ?? throw _lexer.Error("Type expected");
    }
    public TypeNode? MatchType()
    {
        //I.10.7.2 Type names and arity encoding
        //NS.A`1+B`1[int[], int][]&
        if (_lexer.MatchId() is not string name) {
            return null;
        }
        var type = new BasicTypeNode(name) as TypeNode;
        int numArgs = ParseIntAfterBacktick(name);

        //Nested types
        while (_lexer.Match(TokenType.Plus)) {
            if (_lexer.MatchId() is not string childName) {
                return null;
            }
            type = new NestedTypeNode(type, childName);
            numArgs += ParseIntAfterBacktick(childName);
        }

        //Generic arguments
        if (numArgs > 0) {
            _lexer.Expect(TokenType.LBracket);
            var args = new TypeNode[numArgs];
            for (int i = 0; i < numArgs; i++) {
                if (i != 0) _lexer.Expect(TokenType.Comma);
                args[i] = ParseType();
            }
            _lexer.Expect(TokenType.RBracket);
            type = new TypeSpecNode(type, args);
        }
        
        //Compound types (arrays, pointers, ...)
        while (true) {
            if (_lexer.Match(TokenType.LBracket)) {
                //TODO: multi dim arrays
                _lexer.Expect(TokenType.RBracket);
                type = new ArrayTypeNode(type);
            }
            else if (_lexer.Match(TokenType.Asterisk)) {
                type = new PointerTypeNode(type);
            }
            else if (_lexer.Match(TokenType.Ampersand)) {
                type = new ByrefTypeNode(type);
            }
            else break;
        }
        return type;

        int ParseIntAfterBacktick(string name)
        {
            int idx = name.LastIndexOf('`');
            if (idx < 0) {
                return 0;
            }
            if (!int.TryParse(name.AsSpan(idx + 1), out int val)) {
                throw _lexer.Error("Malformed generic type name: backtick must be followed with an integer.");
            }
            return val;
        }
    }
}