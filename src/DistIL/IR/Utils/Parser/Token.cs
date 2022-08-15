namespace DistIL.IR.Utils.Parser;

internal struct Token
{
    public TokenType Type { get; }
    public object? Value { get; }
    public (int Start, int End) Position { get; }

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
    Number,
    String,

    Indent,
    Dedent,

    LParen, RParen,         // ( )
    LBrace, RBrace,         // { }
    LBracket, RBracket,     // [ ]
    LChevron, RChevron,     // < >

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

    _StartLen2,     // (tokens below are two char long)
    Arrow,          // ->
    DoubleColon,    // ::
}