namespace DistIL.IR.Utils.Parser;

//Program   = Import*  Block*
//Import    = "import"  Id  "from"  Id
//Block     = Id  ":"  (Indent  Inst+  Dedent) | Inst
//Type      = Identifier  ("+"  Identifier)?  ("["  Seq{Type}  "]")?  ("[]" | "*" | "&")*
//Inst      = (Type  Id  "=")?  InstBody
//InstBody  = 
//    "goto"  (Label | (Value "?" Label ":" Label))
//  | "phi"  Seq{"["  Label  "->"  Value  "]"}
//  | ("call" | "callvirt" | "newobj")  Method  "(" Seq{CallArg}? ")"
//  | ("ldfld" | "stfld" | "fldaddr")  Field  Operands
//  | Opcode  Operands
//  | Type  Id  "="  Opcode  Operands
//Operands  = Seq{Value}
//Method    = Type  "::"  Id ("<" Seq{Type} ">")?
//CallArg   = ("this" | Type)  ":"  Value
//Field     = Type  "::"  Id
//Value     = Id | Number | String | "null"
//Seq{R}    = R  (","  R)*
//DelimSeq{Start, End, R} = Start  Seq{R}?  End

/// <summary> Generates an AST from an arbitrary string. </summary>
internal class AstParser
{
    readonly Lexer _lexer;
    readonly ParserContext _ctx;
    readonly List<(ModuleDef Mod, string? Ns)> _imports = new();
    readonly ModuleDef _coreLib;

    public AstParser(ParserContext ctx)
    {
        _lexer = new Lexer(ctx);
        _ctx = ctx;

        _coreLib = _ctx.ResolveModule("System.Private.CoreLib");
        _imports.Add((_coreLib, "System"));
    }

    public ProgramNode ParseProgram()
    {
        while (_lexer.MatchKeyword("import")) {
            string ns = _lexer.ExpectId();
            _lexer.ExpectId("from");
            string modName = _lexer.ExpectId();

            var mod = _ctx.ResolveModule(modName);
            _imports.Add((mod, ns));
        }
        var blocks = new List<BlockNode>();

        while (!_lexer.Match(TokenType.EOF)) {
            blocks.Add(ParseBlock());
        }
        return new ProgramNode(blocks);
    }

    //Block = Id  ":"  (Indent  Inst+  Dedent) | Inst
    public BlockNode ParseBlock()
    {
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

    //Type = Identifier  ("+"  Identifier)?  ("["  Seq{Type}  "]")?  ("[]" | "*" | "&")*
    // ~ "NS.A`1+B`1[int[], int][]&"  ->  "NS.A.B<int[], int>[]&"
    //This loosely follows I.10.7.2 "Type names and arity encoding"
    public TypeDesc ParseType()
    {
        int start = _lexer.NextPos();
        string name = _lexer.ExpectId();
        var type = ResolveType(name);

        //Nested types
        while (_lexer.Match(TokenType.Plus)) {
            string childName = _lexer.ExpectId();
            type = (type as TypeDef)?.GetNestedType(childName);
        }
        if (type == null) {
            _lexer.Error("Type could not be found", start);
            return PrimType.Void;
        }
        //Generic arguments
        if (type.IsGeneric && _lexer.IsNext(TokenType.LBracket)) {
            var args = ImmutableArray.CreateBuilder<TypeDesc>();
            ParseDelimSeq(TokenType.LBracket, TokenType.RBracket, () => {
                args.Add(ParseType());
            });
            type = ((TypeDef)type).GetSpec(args.TakeImmutable());
        }
        //Compound types (array, pointer, byref)
        while (true) {
            if (_lexer.Match(TokenType.LBracket)) {
                //TODO: multi dim arrays
                _lexer.Expect(TokenType.RBracket);
                type = type.CreateArray();
            }//
            else if (_lexer.Match(TokenType.Asterisk)) {
                type = type.CreatePointer();
            }//
            else if (_lexer.Match(TokenType.Ampersand)) {
                type = type.CreateByref();
            }//
            else break;
        }
        return type;
    }

    private TypeDesc? ResolveType(string name)
    {
        int nsEnd = name.LastIndexOf('.');
        if (nsEnd < 0) {
            var prim = PrimType.GetFromAlias(name);
            if (prim != null) {
                return prim;
            }
            foreach (var (mod, ns) in _imports) {
                var type = mod.FindType(ns, name);
                if (type != null) {
                    return type;
                }
            }
        } else {
            return _coreLib.FindType(name[0..nsEnd], name[(nsEnd + 1)..]) ??
                throw new NotImplementedException("Fully qualified type name");
        }
        return null;
    }

    public InstNode ParseInst()
    {
        string? slotName = null;
        TypeDesc? slotType = null;

        if (ParseOpcode() is not string opcode) {
            slotType = ParseType();
            slotName = _lexer.ExpectId();
            _lexer.Expect(TokenType.Equal);
            opcode = _lexer.ExpectId();

            if (Materializer.OpcodeHasNoResult(opcode)) {
                _lexer.Error("Slot cannot be declared for instruction with no result");
            }
        }
        var opers = new List<Node>();

        switch (opcode) {
            case "goto": {
                ParseGoto(opers);
                break;
            }
            case "phi": {
                ParsePhi(opers);
                break;
            }
            case "call" or "callvirt" or "newobj": {
                ParseCall(opers, slotType ?? PrimType.Void);
                break;
            }
            case "ldfld" or "stfld" or "fldaddr": {
                opers.Add(new BoundNode(ParseField()!));
                if (_lexer.Match(TokenType.Comma)) {
                    ParseOperands(opers);
                }
                break;
            }
            default: {
                ParseOperands(opers);
                break;
            }
        }
        return new InstNode(opcode, opers, slotType, slotName);
    }

    private string? ParseOpcode()
    {
        var token = _lexer.Peek();
        if (token.Type == TokenType.Identifier && Materializer.IsValidOpcode(token.StrValue)) {
            _lexer.Next();
            return token.StrValue;
        }
        return null;
    }

    private IdNode ParseId()
    {
        return new IdNode(_lexer.ExpectId());
    }

    //DelimSeq{T} = Start  Seq{T}?  End
    //Seq{T} = T  (","  T)*
    private void ParseDelimSeq(TokenType start, TokenType end, Action parseElem)
    {
        _lexer.Expect(start);

        if (!_lexer.Match(end)) {
            do {
                parseElem();
            } while (_lexer.Match(TokenType.Comma));
            _lexer.Expect(end);
        }
    }

    private Node ParseValue()
    {
        var token = _lexer.Next();

        switch (token.Type) {
            case TokenType.Identifier when token.StrValue is "null":
                return new BoundNode(ConstNull.Create());
            case TokenType.Identifier:
                return new IdNode(token.StrValue);
            case TokenType.Literal:
                return new BoundNode((Const)token.Value!);
            default:
                _lexer.Error("Value expected");
                return new BoundNode(null!);
        }
    }

    //Seq{Value} "\n"?
    private void ParseOperands(List<Node> opers)
    {
        if (!_lexer.IsNextOnNewLine()) {
            do {
                opers.Add(ParseValue());
            } while (_lexer.Match(TokenType.Comma));
        }
    }

    //Goto = Label | (Value "?" Label ":" Label)
    private void ParseGoto(List<Node> opers)
    {
        //goto T  |  goto V ? T : F
        var oper1 = ParseValue();
        opers.Add(oper1);
        if (_lexer.Match(TokenType.QuestionMark)) {
            opers.Add(ParseId());
            _lexer.Expect(TokenType.Colon);
            opers.Add(ParseId());
        }
    }

    //Phi = Seq{"["  Label  "->"  Value  "]"}
    // ~ "[Label -> Value], ..."
    private void ParsePhi(List<Node> opers)
    {
        do {
            if (!_lexer.Expect(TokenType.LBracket)) break;

            opers.Add(ParseId());
            _lexer.Expect(TokenType.Arrow);
            opers.Add(ParseValue());
            _lexer.Expect(TokenType.RBracket);
        } while (_lexer.Match(TokenType.Comma));
    }

    private void ParseCall(List<Node> instOpers, TypeDesc retType)
    {
        //Method = Type  "::"  Id  GenArgs  Call
        int start = _lexer.Peek().Position.Start;
        var ownerType = ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        var name = _lexer.ExpectId();

        //GenArgs = ("<" Seq{Type} ">")?
        var genPars = ImmutableArray.CreateBuilder<TypeDesc>();
        if (_lexer.IsNext(TokenType.LAngle)) {
            ParseDelimSeq(TokenType.LAngle, TokenType.RAngle, () => {
                genPars.Add(ParseType());
            });
        }
        //Call    = "(" Seq{CallArg}? ")"
        //CallArg = ("this" | Type)  ":"  Value
        var pars = ImmutableArray.CreateBuilder<TypeDesc>();
        ParseDelimSeq(TokenType.LParen, TokenType.RParen, () => {
            pars.Add(_lexer.MatchKeyword("this") ? ownerType : ParseType());
            _lexer.Expect(TokenType.Colon);
            instOpers.Add(ParseValue());
        });

        var method = ownerType.FindMethod(name, new MethodSig(retType, pars.TakeImmutable(), genPars.Count));
        if (method == null) {
            _lexer.Error("Method could not be found", start);
        } else if (genPars.Count > 0) {
            method = method.GetSpec(new GenericContext(methodArgs: genPars.TakeImmutable()));
        }
        instOpers.Insert(0, new BoundNode(method!));
    }

    private FieldDesc? ParseField()
    {
        var ownerType = ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        string name = _lexer.ExpectId();

        var field = ownerType.FindField(name);
        if (field == null) {
            _lexer.Error("Field could not be found");
        }
        return field;
    }
}