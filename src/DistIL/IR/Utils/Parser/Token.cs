namespace DistIL.IR.Utils.Parser;

internal struct Token
{
    public TokenType Type { get; }
    public object? Value { get; }
    public AbsRange Position { get; }

    public string StrValue => Value as string ?? throw new InvalidOperationException();

    public Token(TokenType type, int startPos, int endPos, object? value = null)
    {
        Type = type;
        Position = (startPos, endPos);
        Value = value;
    }
    public override string ToString() => $"{Type}{(Value == null ? "" : " '" + Value + "'")} at {Position}";
}
internal enum TokenType
{
    EOF,
    Identifier,
    Literal,               //ConstInt|ConstFloat|ConstString

    Indent,
    Dedent,

    LParen, RParen,         // ( )
    LBrace, RBrace,         // { }
    LBracket, RBracket,     // [ ]
    LAngle, RAngle,         // < >

    Plus,           // +
    Minus,          // -
    Asterisk,       // *
    Ampersand,      // &

    Equal,          // =
    Comma,          // ,
    Dot,            // .
    Semicolon,      // ;
    Colon,          // :
    QuestionMark,   // ?
    ExlamationMark, // !
    Caret,          // ^

    _StartLen2,     // (tokens below are two char long)
    Arrow,          // ->
    DoubleColon,    // ::
}