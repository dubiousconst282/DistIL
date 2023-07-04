namespace DistIL.IR.Utils;

using DistIL.IR.Utils.Parser;

using MethodAttribs = System.Reflection.MethodAttributes;

//Unit      := Import*  Method*
//Import    := "import" ( Seq{Id} "from" Id  |  Id "=" Type )
//Method    := MethodAcc  Type "::" Identifier "(" Seq{Param} ")" [ResultType] "{"  VarDeclBlock?  Block*  "}"
//MethodAcc := ("public" | "internal"| "protected" | "private")? "static"? "special"?
//Param     := "this" | (#Id: Type)
//VarDeclBlock := "$Locals"  ":" (Indent VarDecl+ Dedent) | VarDecl+
//VarDecl   := Seq{Id} ":" Type "^"?
//Block     := Id  ":"  (Indent  Inst+  Dedent) | Inst
//Type      := (GenPar | NamedType)  ("[]" | "*" | "&")*
//NamedType := ("+"  Identifier)*  ("["  Seq{Type}  "]")?
//GenPar    := "!" "!"? Number
//Inst      := (Id  "=")?  InstBody
//InstBody  := 
//    "goto"  (Label | (Value "?" Label ":" Label))
//  | "phi"  Seq{"["  Label  ":"  Value  "]"}  ResultType
//  | ("call" | "callvirt" | "newobj")  Method  "(" Seq{CallArg} ")"  ResultType
//  | ("fldaddr")  Field  Value?  ResultType
//  | "stfld"  Field Value ["," Value]
//  | Opcode  Operands
//  | Id  "="  Opcode  Operands  ResultType
//Operands  := Seq{Value}
//ResultType:= "->" Type
//Method    := Type  "::"  Id ("<" Seq{Type} ">")?
//CallArg   := ("this" | Type)  ":"  Value
//Field     := Type  "::"  Id
//Value     := Id | Number | String | "null"
//Seq{R}    := R  (","  R)*

/// <summary> Parser for textual-form IR. </summary>
public partial class IRParser
{
    readonly Lexer _lexer;
    readonly ParserContext _ctx;
    readonly List<(ModuleDef Mod, string? Ns)> _imports = new();
    readonly Dictionary<string, TypeDesc> _typeAliases = new();
    readonly Dictionary<string, Value> _identifiers = new();
    readonly HashSet<PendingInst> _pendingInsts = new();

    MethodBody? _method;
    TypeDesc? _parsedResultType;

    public ParserContext Context => _ctx;

    internal IRParser(ParserContext ctx)
    {
        _lexer = new Lexer(ctx);
        _ctx = ctx;

        _imports.Add((ctx.ModuleResolver.CoreLib, "System"));
    }

    /// <summary> Parses the source code and populates existing definitions with declarations. </summary>
    public static ParserContext Populate(string code, ModuleResolver modResolver)
    {
        var ctx = new ParserContext(code, modResolver);
        try {
            new IRParser(ctx).ParseUnit();
        } catch (FormatException) {
            //Preserve context and let caller handle errors
        }
        return ctx;
    }

    public void ParseUnit()
    {
        ParseImports();

        while (!_lexer.Match(TokenType.EOF)) {
            ParseMethod();
        }
    }

    private void ParseImports()
    {
        while (_lexer.MatchKeyword("import")) {
            var namespaces = new List<string?>();
            do {
                string ns = _lexer.ExpectId();
                namespaces.Add(ns == "$Root" ? null : ns);
            } while (_lexer.Match(TokenType.Comma));

            if (_lexer.MatchKeyword("from")) {
                var module = _ctx.ImportModule(_lexer.ExpectId());
                _imports.AddRange(namespaces.Select(ns => (module, ns))!);
            } else {
                if (namespaces is not [string]) {
                    _lexer.Error("Expected import alias name");
                }
                _lexer.Expect(TokenType.Equal);

                var type = ParseType();
                _typeAliases.Add(namespaces[0]!, type);
            }
        }
    }

    public MethodBody ParseMethod()
    {
        //Method    := MethodAcc Type  Type "::" Identifier "("  Seq{Param}  ")"  "{"  Block*  "}"
        //MethodAcc := ("public" | "internal"| "protected" | "private")? "static"? "special"?

        var access = ParseMethodAcc();

        var parentType = (TypeDef)ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        string name = _lexer.ExpectId();
        
        var paramSig = ImmutableArray.CreateBuilder<ParamDef>();

        if ((access & MethodAttribs.Static) == 0) {
            paramSig.Add(new ParamDef(parentType, "this"));
        }
        //Param := "#" Id ":" Type
        ParseDelimSeq(TokenType.LParen, TokenType.RParen, () => {
            string name = _lexer.ExpectId().TrimStart('#');
            _lexer.Expect(TokenType.Colon);
            var type = ParseType();
            paramSig.Add(new ParamDef(type, name));
        });
        var returnType = _lexer.Match(TokenType.Arrow) ? ParseType() : PrimType.Void;

        var body = _ctx.DeclareMethod(parentType, name, returnType, paramSig.ToImmutable(), default, access);

        SetCurrentMethod(body);
        
        _lexer.Expect(TokenType.LBrace);
        ParseVarDecls();

        while (!_lexer.Match(TokenType.RBrace)) {
            ParseBlock();
        }
        SetCurrentMethod(null);

        return body;
    }

    /// <summary> Sets the method in which blocks are being parsed for. Must be called once before ParseBlock(), and finally with <see langword="null"/> after all blocks have been parsed. </summary>
    public void SetCurrentMethod(MethodBody? method)
    {
        Ensure.That(_method != method);

        if (method == null) {
            foreach (var (id, value) in _identifiers) {
                if (value is PendingValue pv) {
                    throw _ctx.Fatal($"Unassigned identifier '{id}'", pv.Position);
                }
            }
            Debug.Assert(_pendingInsts.Count == 0);
            _identifiers.Clear();
            _pendingInsts.Clear();
        }
        _method = method;
    }

    //Block := Id  ":"  (Indent  Inst+  Dedent) | Inst
    public BasicBlock ParseBlock()
    {
        var block = ParseLabel();
        _lexer.Expect(TokenType.Colon);
        ParseIndentedBlock(() => block.InsertLast(ParseInst()));
        return block;
    }

    private void ParseVarDecls()
    {
        if (!_lexer.MatchKeyword("$Locals")) return;

        _lexer.Expect(TokenType.Colon);

        ParseIndentedBlock(() => {
            //VarDecl  :=  Seq{Id} ":" Type "^"?
            var tokens = new List<Token>();
            do {
                tokens.Add(_lexer.Expect(TokenType.Identifier));
            } while (_lexer.Match(TokenType.Comma));

            _lexer.Expect(TokenType.Colon);

            var type = ParseType();
            bool pinned = _lexer.Match(TokenType.Caret);

            foreach (var token in tokens) {
                var slot = new LocalSlot(type, token.StrValue, pinned);
                AssignId(token, slot, "$" + token.StrValue);
            }
        });
    }

    private void ParseIndentedBlock(Action parseItem)
    {
        if (_lexer.Match(TokenType.Indent)) {
            while (!_lexer.Match(TokenType.Dedent) && !_lexer.Match(TokenType.EOF)) {
                parseItem();
            }
        } else {
            parseItem();
        }
    }

    //Type      := (GenPar | NamedType)  ("[]" | "*" | "&")*
    //NamedType := ("+"  Identifier)*  ("["  Seq{Type}  "]")?
    //GenPar    := "!" "!"? Number
    //e.g. "NS.A`1+B`1[int[], int][]&"  ->  "NS.A.B<int[], int>[]&"
    //This loosely follows I.10.7.2 "Type names and arity encoding"
    public TypeDesc ParseType()
    {
        int start = _lexer.NextPos();
        TypeDesc? type = null;

        if (_lexer.Match(TokenType.ExlamationMark)) {
            bool isMethodParam = _lexer.Match(TokenType.ExlamationMark);
            var indexToken = _lexer.Expect(TokenType.Literal);

            if (indexToken.Value is ConstInt cs) {
                type = GenericParamType.GetUnbound((int)cs.Value, isMethodParam);
            }
        } else {
            string name = _lexer.ExpectId();
            type = ResolveType(name);

            //Nested types
            while (_lexer.Match(TokenType.Plus)) {
                string childName = _lexer.ExpectId();
                type = (type as TypeDef)?.FindNestedType(childName);
            }
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
        if (_typeAliases.TryGetValue(name, out var aliasedType)) {
            return aliasedType;
        }

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
            return _ctx.ModuleResolver.CoreLib.FindType(name[0..nsEnd], name[(nsEnd + 1)..]) ??
                throw new NotImplementedException("Fully qualified type name");
        }
        return null;
    }

    public Instruction ParseInst()
    {
        _parsedResultType = null;
        var opToken = _lexer.Expect(TokenType.Identifier);
        var slotToken = default(Token);

        if (_lexer.Match(TokenType.Equal)) {
            slotToken = opToken;
            opToken = _lexer.Expect(TokenType.Identifier);
        }
        var (op, mods) = Opcodes.TryParse(opToken.StrValue);

        var inst = op switch {
            Opcode.Goto => ParseGoto(),
            Opcode.Switch => ParseSwitch(),
            Opcode.Ret => ParseRet(),
            Opcode.Phi => ParsePhi(),
            Opcode.Call or Opcode.CallVirt or Opcode.NewObj => ParseCallInst(op),
            Opcode.FldAddr => ParseFieldAddr(mods),
            Opcode.ArrAddr => ParseArrayAddr(mods),
            Opcode.Load or Opcode.Store => ParseMemInst(op, mods),
            Opcode.Conv => ParseConv(op, mods),
            _ => ParseMultiOpInst(op, mods, opToken.Position),
        };
        if (slotToken.Type == TokenType.Identifier) {
            AssignId(slotToken, inst);
        }

        if (_parsedResultType == null && inst.ResultType != PrimType.Void) {
            ParseResultType();
        }
        if (inst.ResultType != (_parsedResultType ?? PrimType.Void)) {
            _lexer.Error("Declared slot type does not match instruction result type");
        }
        return inst;
    }
    private TypeDesc ParseResultType()
    {
        _lexer.Expect(TokenType.Arrow);
        return _parsedResultType = ParseType();
    }

    private Instruction ParseMultiOpInst(Opcode op, OpcodeModifiers mods, AbsRange pos)
    {
        if (op is > Opcode._Bin_First and < Opcode._Bin_Last) {
            return Schedule(PendingInst.Kind.Binary, op - (Opcode._Bin_First + 1));
        }
        if (op is > Opcode._Cmp_First and < Opcode._Cmp_Last) {
            return Schedule(PendingInst.Kind.Compare, op - (Opcode._Cmp_First + 1));
        }
        throw _ctx.Fatal("Unknown instruction", pos);

        //Some insts have dynamic result types and depend on the real value type,
        //once they're found, we'll materialize them.
        Instruction Schedule(PendingInst.Kind kind, int op)
        {
            var left = ParseValue();
            _lexer.Expect(TokenType.Comma);
            var right = ParseValue();
            var type = ParseResultType();

            if (PendingInst.Resolve(kind, op, left, right) is { } resolved) {
                return resolved;
            }
            var inst = new PendingInst(kind, op, left, right, type);
            _pendingInsts.Add(inst);
            return inst;
        }
    }

    //Goto := Label | (Value "?" Label ":" Label)
    private BranchInst ParseGoto()
    {
        if (LookaheadValue(TokenType.QuestionMark, out var cond)) {
            var thenBlock = ParseLabel();
            _lexer.Expect(TokenType.Colon);
            var elseBlock = ParseLabel();
            return new BranchInst(cond, thenBlock, elseBlock);
        }
        return new BranchInst(ParseLabel());
    }

    //Switch := Value  ","  "[" Indent?  SwitchCase+  Dedent? "]"
    //SwitchCase = ("_" | Label) ":" Label
    private SwitchInst ParseSwitch()
    {
        var value = ParseValue();
        var defaultCase = default(BasicBlock);
        var cases = new List<BasicBlock>();

        _lexer.Expect(TokenType.Comma);
        _lexer.Expect(TokenType.LBracket);
        bool hasIndent = _lexer.Match(TokenType.Indent);

        do {
            var caseToken = _lexer.Next();
            _lexer.Expect(TokenType.Colon);
            var caseTarget = ParseLabel();

            if (caseToken is { Type: TokenType.Identifier, StrValue: "_" }) {
                if (defaultCase != null) {
                    _ctx.Error("Switch cannot have more than one default case", caseToken.Position);
                }
                defaultCase = caseTarget;
            } else {
                if (caseToken.Value is not ConstInt c || c.Value != cases.Count) {
                    _ctx.Error("Switch case must be a sequential integer", caseToken.Position);
                }
                cases.Add(caseTarget);
            }
        } while (_lexer.Match(TokenType.Comma));

        if (hasIndent) _lexer.Expect(TokenType.Dedent);
        var lastToken = _lexer.Expect(TokenType.RBracket);

        if (defaultCase == null) {
            throw _ctx.Fatal("Switch must have a default case", lastToken.Position);
        }
        return new SwitchInst(value, defaultCase, cases.ToArray());
    }

    //Ret := Value?
    private ReturnInst ParseRet()
    {
        var value = _lexer.IsNextOnNewLine() ? null : ParseValue();
        return new ReturnInst(value);
    }

    //Phi := Seq{"["  Label  ":"  Value  "]"} ResultType
    //e.g. "[Label -> Value], ..."
    private PhiInst ParsePhi()
    {
        var args = new List<Value>();
        do {
            _lexer.Expect(TokenType.LBracket);

            args.Add(ParseLabel());
            _lexer.Expect(TokenType.Colon);

            args.Add(ParseValue());
            _lexer.Expect(TokenType.RBracket);
        } while (_lexer.Match(TokenType.Comma));

        var type = ParseResultType();

        return new PhiInst(type, args.ToArray());
    }

    private Instruction ParseCallInst(Opcode op)
    {
        //Method := Type  "::"  Id  GenArgs  Call  ResultType
        int start = _lexer.Peek().Position.Start;
        var ownerType = ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        var name = _lexer.ExpectId();

        //GenArgs := ("<" Seq{Type} ">")?
        var genPars = new List<TypeDesc>();
        if (_lexer.IsNext(TokenType.LAngle)) {
            ParseDelimSeq(TokenType.LAngle, TokenType.RAngle, () => {
                genPars.Add(ParseType());
            });
        }
        //Call    := "(" Seq{CallArg}? ")"
        //CallArg := ("this" | Type)  ":"  Value
        var pars = new List<TypeSig>();
        var opers = new List<Value>();
        bool isInstance = false;

        ParseDelimSeq(TokenType.LParen, TokenType.RParen, () => {
            if (_lexer.MatchKeyword("this")) {
                isInstance = true;
            } else {
                pars.Add(ParseType());
            }
            _lexer.Expect(TokenType.Colon);
            opers.Add(ParseValue());
        });
        var retType = _lexer.IsNext(TokenType.Arrow) ? ParseResultType() : PrimType.Void;

        var sig = new MethodSig(retType, pars, isInstance, genPars.Count);
        var method = ownerType.FindMethod(name, sig, throwIfNotFound: false)
                ?? throw _ctx.Fatal("Method could not be found", (start, _lexer.LastPos()));

        if (genPars.Count > 0) {
            method = method.GetSpec(new GenericContext(methodArgs: genPars));
            _parsedResultType = method.ReturnType;
        }

        if (op == Opcode.NewObj) {
            return new NewObjInst(method, opers.ToArray());
        }
        return new CallInst(method, opers.ToArray(), op == Opcode.CallVirt);
    }

    private Instruction ParseFieldAddr(OpcodeModifiers mods)
    {
        int start = _lexer.NextPos();
        var declType = ParseType();
        _lexer.Expect(TokenType.DoubleColon);
        string name = _lexer.ExpectId();

        var field = declType.FindField(name)
                ?? throw _ctx.Fatal("Field could not be found", (start, _lexer.LastPos()));

        var obj = _lexer.Match(TokenType.Comma) ? ParseValue() : null;

        return new FieldAddrInst(field, obj);
    }

    private Instruction ParseConv(Opcode op, OpcodeModifiers mods)
    {
        var srcValue = ParseValue();
        bool checkOvf = (mods & OpcodeModifiers.Ovf) != 0;
        bool unsigned = (mods & OpcodeModifiers.Un) != 0;
        var type = ParseResultType();

        return new ConvertInst(srcValue, type, checkOvf, unsigned);
    }

    private Instruction ParseMemInst(Opcode op, OpcodeModifiers mods)
    {
        var address = ParseValue();
        var flags = PointerFlags.None;
        flags |= (mods & OpcodeModifiers.Un) != 0 ? PointerFlags.Unaligned : 0;
        flags |= (mods & OpcodeModifiers.Volatile) != 0 ? PointerFlags.Volatile : 0;

        switch (op) {
            case Opcode.Load: {
                var type = ParseResultType();
                return new LoadInst(address, type, flags);
            }
            case Opcode.Store: {
                _lexer.Expect(TokenType.Comma);
                var value = ParseValue();
                var type = _lexer.MatchKeyword("as") ? ParseType() : null;
                return new StoreInst(address, value, type, flags);
            }
            default: throw new UnreachableException();
        }
    }

    private Instruction ParseArrayAddr(OpcodeModifiers mods)
    {
        var array = ParseValue();
        _lexer.Expect(TokenType.Comma);
        var index = ParseValue();

        var type = ParseResultType();

        return new ArrayAddrInst(
            array, index, 
            elemType: ((ByrefType)type).ElemType,
            inBounds: mods.HasFlag(OpcodeModifiers.InBounds),
            readOnly: mods.HasFlag(OpcodeModifiers.ReadOnly));
    }

    private BasicBlock ParseLabel()
    {
        var token = _lexer.Expect(TokenType.Identifier);
        return AllocId(token, () => _method!.CreateBlock());
    }

    private Value ParseValue()
    {
        var token = _lexer.Next();
        return ParseValue(token);
    }

    private Value ParseValue(Token token)
    {
        switch (token.Type) {
            case TokenType.Identifier when token.StrValue is "null": {
                return ConstNull.Create();
            }
            case TokenType.Identifier: {
                return AllocId<Value>(token, () => {
                    string name = token.StrValue;

                    if (name.StartsWith("#")) {
                        if (name == "#this" && _method!.Definition.IsInstance) {
                            return _method.Args[0];
                        }
                        return _method!.Args.FirstOrDefault(a => a.Name == name[1..])
                            ?? throw _ctx.Fatal($"Unknown argument '{name}'", token.Position);
                    }
                    if (name.StartsWith("$")) {
                        _ctx.Error($"Undeclared local variable '{name}'", token.Position);
                    }
                    return new PendingValue() { Position = token.Position };
                });
            }
            case TokenType.Literal: {
                return (Const)token.Value!;
            }
            default: {
                _lexer.ErrorUnexpected(token, "Value");
                return new Undef(PrimType.Void);
            }
        }
    }

    private bool LookaheadValue(TokenType matchType, [NotNullWhen(true)] out Value? value)
    {
        var prevCursor = _lexer.Cursor;
        var token = _lexer.Next();

        if (token.Type is not TokenType.Identifier or TokenType.Literal) {
            _lexer.ErrorUnexpected(token, "Value");
        }//
        else if (_lexer.Match(matchType)) {
            value = ParseValue(token);
            return true;
        }
        _lexer.Cursor = prevCursor;
        value = null;
        return false;
    }

    private V AllocId<V>(Token token, Func<V> createNew) where V : Value
    {
        ref var slot = ref _identifiers.GetOrAddRef(token.StrValue);

        slot ??= createNew().SetName(token.StrValue);

        return slot as V
            ?? throw _ctx.Fatal($"Cannot reserve identifier '{token.StrValue}' to a '{typeof(V).Name}' because it is already assigned to a '{slot.GetType().Name}'", token.Position);
    }
    private void AssignId(Token id, Value value, string? name = null)
    {
        ref var slot = ref _identifiers.GetOrAddRef(name ?? id.StrValue);

        if (slot is PendingValue placeholder) {
            foreach (var use in placeholder.Uses()) {
                use.Operand = value;

                if (use.Parent is PendingInst inst && inst.TryResolve()) {
                    _pendingInsts.Remove(inst);
                }
            }
        } else if (slot != null) {
            throw _ctx.Fatal($"Identifier '{id.StrValue}' was already assigned to a different value", id.Position);
        }
        slot = value;
    }

    //DelimSeq{T} := Start  Seq{T}?  End
    //Seq{T}      := T  (","  T)*
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

    private MethodAttribs ParseMethodAcc()
    {
        var attribs = default(MethodAttribs);

        if (_lexer.MatchKeyword("public")) {
            attribs |= MethodAttribs.Public;
        }//
        else if (_lexer.MatchKeyword("internal")) {
            attribs |= MethodAttribs.Assembly;
        }//
        else if (_lexer.MatchKeyword("protected")) {
            attribs |= MethodAttribs.Family;
        }//
        else {
            _lexer.MatchKeyword("private");
            attribs |= MethodAttribs.Private;
        }

        if (_lexer.MatchKeyword("static")) {
            attribs |= MethodAttribs.Static;
        }
        if (_lexer.MatchKeyword("special")) {
            attribs |= MethodAttribs.SpecialName;
        }
        return attribs;
    }


    sealed class PendingValue : TrackedValue
    {
        public AbsRange Position;

        public override void Print(PrintContext ctx) => ctx.Print($"pending(first ref={Position.ToString()})");
    }
    sealed class PendingInst : Instruction
    {
        public Kind InstKind;
        public int Op;

        public override string InstName => "pending." + InstKind;
        public override void Accept(InstVisitor visitor) => throw new InvalidOperationException();

        public PendingInst(Kind kind, int op, Value left, Value right, TypeDesc resultType)
            : base(left, right)
            => (InstKind, Op, ResultType) = (kind, op, resultType);

        public bool TryResolve()
        {
            if (Resolve(InstKind, Op, Operands[0], Operands[1]) is { } resolved) {
                Ensure.That(resolved.ResultType == ResultType);
                ReplaceWith(resolved, insertIfInst: true);
                return true;
            }
            return false;
        }

        public static Instruction? Resolve(Kind kind, int op, Value left, Value right)
        {
            if (left is PendingValue || right is PendingValue) {
                return null;
            }
            return kind switch {
                PendingInst.Kind.Binary => new BinaryInst((BinaryOp)op, left, right),
                PendingInst.Kind.Compare => new CompareInst((CompareOp)op, left, right)
            };
        }

        public enum Kind { Binary, Compare };
    }
}