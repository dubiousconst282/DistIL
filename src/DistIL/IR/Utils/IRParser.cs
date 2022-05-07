namespace DistIL.IR.Utils;

using System.Globalization;
using System.Text.RegularExpressions;

public class IRParser
{
    Dictionary<string, Undef> _labels = new();
    Lexer _lexer;

    private void ParseBlock()
    {

    }
    private Instruction ParseInst()
    {
        var name = ParseInstName();

        return null;
    }

    private string ParseInstName()
    {
        string s = _lexer.Expect(TokenType.Identifier).StrValue;
        //Parse names like "icmp.lt"
        //Using string concat directly because these names are uncommon.
        while (_lexer.Match(TokenType.Dot)) {
            s += _lexer.Expect(TokenType.Identifier).StrValue;
        }
        return s;
    }

    static BinaryOp? GetBinaryOp(string name) => name switch {
        "add"   => BinaryOp.Add,
        "sub"   => BinaryOp.Sub,
        "mul"   => BinaryOp.Mul,
        "sdiv"  => BinaryOp.SDiv,
        "srem"  => BinaryOp.SRem,
        "udiv"  => BinaryOp.UDiv,
        "urem"  => BinaryOp.URem,
        "and"   => BinaryOp.And,
        "or"    => BinaryOp.Or,
        "xor"   => BinaryOp.Xor,
        "shl"   => BinaryOp.Shl,
        "shra"  => BinaryOp.Shra,
        "shrl"  => BinaryOp.Shrl,
        "fadd"  => BinaryOp.FAdd,
        "fsub"  => BinaryOp.FSub,
        "fmul"  => BinaryOp.FMul,
        "fdiv"  => BinaryOp.FDiv,
        "frem"  => BinaryOp.FRem,
        "add.ovf" => BinaryOp.AddOvf,
        "sub.ovf" => BinaryOp.SubOvf,
        "mul.ovf" => BinaryOp.MulOvf,
        "uadd.ovf" => BinaryOp.UAddOvf,
        "usub.ovf" => BinaryOp.USubOvf,
        "umul.ovf" => BinaryOp.UMulOvf,
        _ => null
    };

    class Lexer
    {
        private string _str;
        private int _pos;
        private int _startPos; //start position of the current token
        private Token? _peeked;

        public Lexer(string str) => _str = str;

        public bool Match(TokenType type) => Match(type, out _);
        public bool Match(TokenType type, out Token token)
        {
            token = Peek();
            if (token.Type == type) {
                _peeked = null;
                return true;
            }
            return false;
        }
        public bool MatchKeyword(string keyword)
        {
            var token = Peek();
            if (token.Type == TokenType.Identifier && token.StrValue == keyword) {
                _peeked = null;
                return true;
            }
            return false;
        }
        public Token Expect(TokenType type)
        {
            var token = Next();
            if (token.Type == type) {
                return token;
            }
            throw Error($"Expected '{type}', got '{token.Type}'");
        }
        public bool IsNext(TokenType type)
        {
            return Peek().Type == type;
        }

        //Returns the position of the next token
        public int GetPos() => _peeked?.Position.Start ?? _pos;
        public void SetPos(int pos)
        {
            _startPos = _pos = pos;
            _peeked = null;
        }

        public Token Peek()
        {
            _peeked ??= Next();
            return _peeked.Value;
        }
        public Token Next()
        {
            if (_peeked != null) {
                var token = _peeked.Value;
                _peeked = null;
                return token;
            }
            if (!SkipWhitespace()) {
                return new Token(TokenType.EOF, _pos, _pos);
            }
            _startPos = _pos;
            Token Tok(TokenType type, object? value = null)
                => new Token(type, _startPos, _pos, value);

            char ch = _str[_pos];

            var type1 = ch switch {
                '(' => TokenType.LParen,
                ')' => TokenType.RParen,
                '{' => TokenType.LBrace,
                '}' => TokenType.RBrace,
                '[' => TokenType.LBracket,
                ']' => TokenType.RBracket,
                ',' => TokenType.Comma,
                '.' => TokenType.Dot,
                ';' => TokenType.Semicolon,
                ':' => TokenType.Colon,
                '?' => TokenType.QuestionMark,
                _  => TokenType.EOF
            };
            if (type1 != TokenType.EOF) {
                return Tok(type1);
            }
            if (ch is '-' or (>= '0' and <= '9')) {
                return Tok(TokenType.Number, ParseNumber());
            }
            if (IsIdentifierChar(ch)) {
                string str = ParseIdentifier();
                return Tok(TokenType.Identifier, str);
            }
            throw Error("Unknown character");
        }
        
        //int [.fract] [E|e [+|-] exp] [U|L|UL|F|D]
        static readonly Regex _numberRegex = new(@"\d+(\.\d+)?([Ee][+-]?\d+)?(U|L|UL|F|D)?", RegexOptions.IgnoreCase);
        private Const ParseNumber()
        {
            var m = _numberRegex.Match(_str, _pos);
            if (!m.Success) {
                throw Error("Malformed number");
            }
            _pos += m.Length;

            string postfix = m.Groups[3].Value;

            if (m.Groups[1].Success || m.Groups[2].Success) { //fraction or exponent
                double r = double.Parse(m.ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture);

                bool F = postfix.EqualsIgnoreCase("F");
                var type = F ? PrimType.Single : PrimType.Double;

                return ConstFloat.Create(type, r);
            } else {
                long r = long.Parse(m.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture);

                bool U = postfix.ContainsIgnoreCase("U");
                bool L = postfix.ContainsIgnoreCase("L");
                var type = L 
                    ? (U ? PrimType.UInt64 : PrimType.Int64) 
                    : (U ? PrimType.UInt32 : PrimType.Int32);

                return ConstInt.Create(type, r);
            }
        }
        private string ParseIdentifier()
        {
            while (_pos < _str.Length && IsIdentifierChar(_str[_pos])) _pos++;
            return _str[_startPos.._pos];
        }

        private static bool IsIdentifierChar(char ch)
        {
            return ch is
                (>= 'a' and <= 'z') or
                (>= 'A' and <= 'Z') or
                (>= '0' and <= '9') or
                '_';
        }

        //Moves _pos to the start of the next token, skipping whitespace and comments.
        private bool SkipWhitespace()
        {
            while (_pos < _str.Length) {
                if (char.IsWhiteSpace(_str[_pos])) {
                    _pos++;
                    continue;
                }
                if (!SkipComments()) {
                    return true;
                }
            }
            return false;
        }
        private bool SkipComments()
        {
            var str = _str.AsSpan(_pos);
            int endPos;

            if (str.StartsWith("//")) {
                endPos = str.IndexOf('\n') + 1;
                if (endPos == 0) endPos = str.Length;
            } else if (str.StartsWith("/*")) {
                endPos = str.IndexOf("*/") + 2;
                if (endPos == 1) throw Error("Unterminated multi-line comment");
            } else {
                return false;
            }
            _pos += endPos;
            return true;
        }

        public Exception Error(string msg) => Error(msg, _startPos, _pos);
        public Exception Error(string msg, Token token) => Error(msg, token.Position.Start, token.Position.End);

        public Exception Error(string msg, int start, int end)
        {
            var (line, col) = GetLinePos(_str, start);
            return new FormatException($"{msg}\nat line {line}, column {col}:\n\n{GetErrorContext(_str, start, end)}");
        }
        private static (int Line, int Column) GetLinePos(string str, int pos)
        {
            int ln = 1, col = 1;
            for (int i = 0; i < pos; i++) {
                if (str[i] == '\n') {
                    ln++;
                    col = 1;
                } else {
                    col++;
                }
            }
            return (ln, col);
        }
        private static string GetErrorContext(string str, int start, int end)
        {
            const int MAX_LEN = 32;
            int tokenLen = end - start;
            int wstart = start, wend = end;

            while (wstart > 0 && (start - wstart) < MAX_LEN) {
                if (str[wstart - 1] == '\n') break;
                wstart--;
            }
            while (wend < str.Length && (end - wend) < MAX_LEN) {
                if (str[wend] == '\n') break;
                wend++;
            }
            string padding = new string(' ', start - wstart);
            string underline = new string('^', Math.Clamp(tokenLen, 0, MAX_LEN - 1));

            return $"{str[wstart..wend]}\n{padding}{underline}{(tokenLen >= MAX_LEN ? "..." : "")}";
        }
    }


    struct Token
    {
        public TokenType Type { get; }
        public object? Value { get; }
        public (int Start, int End) Position { get; }

        public string StrValue => (string)(Value ?? throw new InvalidOperationException());

        public Token(TokenType type, int startPos, int endPos, object? value = null)
        {
            Type = type;
            Position = (startPos, endPos);
            Value = value;
        }
        public override string ToString() => $"{Type}{(Value == null ? "" : " '" + Value + "'")} at {Position}";
    }
    enum TokenType
    {
        EOF,
        Identifier,
        Number,

        LParen, RParen,         // ( )
        LBrace, RBrace,         // { }
        LBracket, RBracket,     // [ ]

        Comma,          // ,
        Dot,            // .
        Semicolon,      // ;
        Colon,          // :
        QuestionMark,   // ?
    }
}