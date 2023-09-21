namespace DistIL.Util;

using System.Text.RegularExpressions;

/// <summary> Directive driven file pattern matcher and comparer. </summary>
/// <remarks>
/// This implements a subset of LLVM's FileCheck tool. See https://www.llvm.org/docs/CommandGuide/FileCheck.html. <para/>
/// Status: <br/>
/// - Supported directives: CHECK, NOT, SAME, NEXT <br/>
/// - Partial support for regex and substitution holes. <br/>
/// - Comments and directive prefixes are currently hardcoded to "//" and "CHECK" respectively. <br/>
/// </remarks>
public class FileChecker
{
    readonly string _source;
    readonly List<Directive> _directives = new();
    readonly bool _hasDynamicPatterns; //whether there may be directives using regex/subst holes.

    static readonly Regex s_DirectiveRegex = new(@"^\s*\/\/\s*CHECK(?:-[A-Z]+)?:.+$", RegexOptions.Multiline | RegexOptions.NonBacktracking);
    static readonly (string K, DirectiveType V)[] s_KnownDirectives = {
        ("CHECK",       DirectiveType.Check),
        ("CHECK-NOT",   DirectiveType.CheckNot),
        ("CHECK-SAME",  DirectiveType.CheckSame),
        ("CHECK-NEXT",  DirectiveType.CheckNext),
    };

    public FileChecker(string source)
    {
        _source = source;

        foreach (var match in s_DirectiveRegex.EnumerateMatches(source)) {
            int start = source.IndexOf("CHECK", match.Index, StringComparison.Ordinal);
            int end = source.IndexOf(':', start);
            var type = s_KnownDirectives.FirstOrDefault(d => source.AsSpan()[start..end].EqualsIgnoreCase(d.K)).V;

            if (type == DirectiveType.Invalid) {
                int lineNo = source.Take(match.Index).Count(c => c == '\n');
                throw new FormatException($"Unknown FileCheck directive '{source[start..end]}' on line {lineNo}");
            }
            _directives.Add(new() {
                Type = type,
                PatternRange = new AbsRange(end + 1, match.Index + match.Length)
            });

            var line = source.AsSpan(end, match.Index + match.Length - end);
            _hasDynamicPatterns |= line.Contains("{{", StringComparison.Ordinal) || line.Contains("[[", StringComparison.Ordinal);
        }

        if (_directives[0].Type is DirectiveType.CheckNext or DirectiveType.CheckSame) {
            throw new FormatException($"{_directives[0].Type} cannot be the first directive in the file.");
        }
    }

    public static FileCheckResult Check(string source, string target, StringComparison compMode)
    {
        return new FileChecker(source).Check(target, compMode);
    }

    public FileCheckResult Check(string text, StringComparison compMode)
    {
        var sc = new CheckScanner() {
            Reader = new LineReader() { Text = text, Pos = 0 },
            Dirs = _directives.AsSpan(),
            Source = _source,
            CompMode = compMode,
            DynEval = _hasDynamicPatterns ? new() : null,
        };
        return sc.CheckInput();
    }

    ref struct CheckScanner
    {
        const int NullPos = int.MaxValue;
        public required LineReader Reader;
        public required ReadOnlySpan<Directive> Dirs;
        public required string Source;
        public required StringComparison CompMode;
        public required DynamicPatternEvaluator? DynEval;

        int _dirPos = 0, _notDirStartPos = NullPos;
        Directive _currDir;
        List<FileCheckFailure>? _failures;

        public CheckScanner() {}

        public FileCheckResult CheckInput()
        {
            Advance();

            while (true) {
                if (Reader.EOF || _currDir.Type == DirectiveType.Invalid) {
                    //Only succeed if there are no more directives or the last one is CHECK-NOT
                    if (_failures == null && _currDir.Type is DirectiveType.Invalid or DirectiveType.CheckNot) {
                        return FileCheckResult.Success;
                    }
                    if (_currDir.Type != DirectiveType.Invalid) {
                        AddFailure(_currDir);
                    }
                    goto Fail;
                }
                Debug.Assert(_currDir.Type is DirectiveType.Check or DirectiveType.CheckNot);

                var currLine = Reader.Next();
                if (MatchExclusions(currLine)) goto Fail;

                if (_currDir.Type == DirectiveType.Check) {
                    if (!Match(currLine, isForPlainCheck: true)) continue;

                    while (_currDir.Type == DirectiveType.CheckSame) {
                        if (!Match(currLine)) goto Fail;
                    }
                    while (_currDir.Type == DirectiveType.CheckNext) {
                        if (Reader.EOF || !Match(Reader.Next())) goto Fail;
                    }
                }
            }
        Fail:
            Debug.Assert(_failures != null);
            return new FileCheckResult(_failures);
        }

        private bool Match(ReadOnlySpan<char> line, bool isForPlainCheck = false)
        {
            if (!isForPlainCheck && MatchExclusions(line)) {
                return false;
            }
            if (MatchDir(line, _currDir)) {
                Advance();
                return true;
            }
            if (!isForPlainCheck) {
                AddFailure(_currDir);
            }
            return false;
        }
        //Checks if line matches any of the preceeding CHECK-NOT directives, if any
        private bool MatchExclusions(ReadOnlySpan<char> line)
        {
            bool trail = _currDir.Type == DirectiveType.CheckNot; //last directive is CHECK-NOT

            for (int i = _notDirStartPos; i < _dirPos - (trail ? 0 : 1); i++) {
                Debug.Assert(Dirs[i].Type == DirectiveType.CheckNot);
                if (MatchDir(line, Dirs[i])) {
                    AddFailure(Dirs[i]);
                    return true;
                }
            }
            return false;
        }
        private bool MatchDir(ReadOnlySpan<char> line, in Directive dir)
        {
            var pattern = Source.AsSpan().Slice(dir.PatternRange);
            return MatchPattern(line, pattern, CompMode, DynEval);
        }
        private void Advance()
        {
            _notDirStartPos = NullPos;
            _currDir.Type = DirectiveType.Invalid;

            while (_dirPos < Dirs.Length) {
                _currDir = Dirs[_dirPos++];

                if (_currDir.Type != DirectiveType.CheckNot) break;
                _notDirStartPos = Math.Min(_notDirStartPos, _dirPos - 1);
            }
        }
        private void AddFailure(in Directive dir)
        {
            var sb = new StringBuilder();

            sb.Append(dir.Type == DirectiveType.CheckNot ? "Found unexpected match" : "No match found");
            sb.Append($" for '{dir.Type}: {Source.AsSpan().Slice(dir.PatternRange)}' directive, near input lines\n\n");

            var inputRange = Reader.GetCurrentRange();
            int startLine = StringExt.GetLinePos(Reader.Text, inputRange.Start).Line;

            sb.Append($"  {startLine}. ");

            if (inputRange.Length > 120) {
                var truncRange = AbsRange.FromSlice(inputRange.Start, 120);
                sb.Append(Reader.Text.Slice(truncRange)).Append("...");
            } else {
                sb.Append(Reader.Text.Slice(inputRange));
            }
            
            _failures ??= new();
            _failures.Add(new FileCheckFailure() {
                DirectivePos = dir.PatternRange,
                InputPos = inputRange,
                Message = sb.ToString()
            });
        }
    }

    /// <summary> Checks if <paramref name="text"/> matches the given pattern. </summary>
    public static bool MatchPattern(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, StringComparison compMode, DynamicPatternEvaluator? dynEval)
    {
        int textWinPos = 0, patternStartPos = 0;
        var firstToken = NextToken(pattern, ref patternStartPos);
        Ensure.That(firstToken.Type != TokenType.EOF, "Pattern cannot be empty");

        if (firstToken.Type != TokenType.Literal) {
            patternStartPos = 0;
        }

        while (true) {
            if (firstToken.Type == TokenType.Literal) {
                //Quickly find the start of a possible literal match.
                int firstMatchOffset = IndexOfLiteral(text, firstToken.Text, textWinPos, compMode);
                if (firstMatchOffset < 0) return false;

                textWinPos = firstMatchOffset + firstToken.Text.Length;
            }
            int textPos = textWinPos;
            int patternPos = patternStartPos;

            //Check for matching tokens
            while (true) {
                var token = NextToken(pattern, ref patternPos);

                if (token.Type is TokenType.RegexHole or TokenType.SubstHole && dynEval != null) {
                    var prefix = text[textPos..].TrimStart();
                    var matchSubRange = dynEval.Match(token, prefix, compMode);
                    if (matchSubRange.IsEmpty) return false;

                    int trimLen = text.Length - textPos - prefix.Length;
                    textPos += matchSubRange.End + trimLen;
                } else {
                    var textToken = NextToken(text, ref textPos, literalOnly: true);

                    if (token.Type == TokenType.EOF || textToken.Type == TokenType.EOF) {
                        //Consider a match only if we have no more pattern tokens.
                        return token.Type == TokenType.EOF;
                    }
                    Debug.Assert(token.Type == TokenType.Literal);

                    if (!token.Text.Equals(textToken.Text, compMode)) {
                        //No more matches, try again on next window alignment.
                        break;
                    }
                }
            }
        }
    }

    private static int IndexOfLiteral(ReadOnlySpan<char> text, ReadOnlySpan<char> lit, int startOffset, StringComparison compMode)
    {
        // Quick path for literals
        while (true) {
            int offset = startOffset + text[startOffset..].IndexOf(lit, compMode);

            if (offset < startOffset) {
                return -1;
            }
            //Make sure that's a full token, not just an affix
            if ((offset <= 0 || Token.IsSeparator(text[offset - 1])) &&
                (offset + lit.Length >= text.Length || Token.IsSeparator(text[offset + lit.Length]))
            ) {
                return offset;
            }
            startOffset = offset + lit.Length;
        }
    }

    internal static Token NextToken(ReadOnlySpan<char> text, scoped ref int pos, bool literalOnly = false)
    {
        //Skip whitespace
        while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;

        int start = pos;

        //Holes: {{regex}} or [[var]]
        if (!literalOnly && start + 1 < text.Length && text[start] is '{' or '[' && text[start] == text[start + 1]) {
            string closer = text[start] == '{' ? "}}" : "]]";
            int closerDist = text.Slice(start + 2).IndexOf(closer, StringComparison.Ordinal);

            if (closerDist > 0) {
                pos = start + closerDist + 4;

                return new Token() {
                    Type = text[start] == '{' ? TokenType.RegexHole : TokenType.SubstHole,
                    Text = text[(start + 2)..(pos - 2)]
                };
            }
        }

        //Normal token: [A-Z0-9_]+|.
        while (pos < text.Length && !Token.IsSeparator(text[pos])) pos++;

        //Ensure we never output empty tokens; treat unknown chars as individual tokens.
        if (pos == start && pos < text.Length) pos++;

        return new Token() {
            Type = pos == start ? TokenType.EOF : TokenType.Literal,
            Text = text[start..pos]
        };
    }

    internal ref struct Token
    {
        public ReadOnlySpan<char> Text;
        public TokenType Type;

        public static bool IsSeparator(char ch) => !char.IsAsciiLetterOrDigit(ch) && ch != '_';

        public override string ToString() => $"{Type}: '{Text}'";
    }
    internal enum TokenType
    {
        EOF,
        Literal,        // text
        RegexHole,      // {{text}}
        SubstHole,      // [[text]]
    }

    public class DynamicPatternEvaluator
    {
        readonly Dictionary<string, Regex> _regexCache = new();
        readonly Dictionary<string, string> _substMap = new();

        internal AbsRange Match(Token hole, ReadOnlySpan<char> text, StringComparison compMode)
        {
            if (hole.Type == TokenType.RegexHole) {
                return MatchRegex(hole.Text, text, compMode);
            }
            //Substitution hole
            Debug.Assert(hole.Type == TokenType.SubstHole);
            int colonIdx = hole.Text.IndexOf(':');

            if (colonIdx < 0) {
                //Use: [[key]]  
                //TODO: this should probably match tokens instead of substr via MatchPattern()
                string key = hole.Text.ToString();

                return _substMap.TryGetValue(key, out string? val) && text.StartsWith(val, compMode) 
                        ? new AbsRange(0, text.Length - text.Length + val.Length) : default;
            } else {
                //Assignment: [[key:pattern]]
                var range = MatchRegex(hole.Text[(colonIdx + 1)..], text, compMode);
                if (!range.IsEmpty) {
                    string key = hole.Text[0..colonIdx].ToString();
                    _substMap[key] = text.Slice(range).ToString();
                }
                return range;
            }
        }

        private AbsRange MatchRegex(ReadOnlySpan<char> pattern_, ReadOnlySpan<char> text, StringComparison compMode)
        {
            var opts = RegexOptions.CultureInvariant;

            if (compMode is StringComparison.InvariantCultureIgnoreCase or StringComparison.OrdinalIgnoreCase) {
                opts |= RegexOptions.IgnoreCase;
            }

            string pattern = pattern_.ToString();
            ref var regex = ref _regexCache.GetOrAddRef(pattern);

            if (regex == null || regex.Options != opts) {
                regex = new Regex("^" + pattern, opts);
            }

            foreach (var m in regex.EnumerateMatches(text)) {
                return AbsRange.FromSlice(m.Index, m.Length);
            }
            return default;
        }
    }

    struct Directive
    {
        public DirectiveType Type;
        public AbsRange PatternRange; //Pattern range in source text
    }
    enum DirectiveType
    {
        Invalid,
        Check,
        CheckNot,
        CheckSame,
        CheckNext,
    }

    ref struct LineReader
    {
        public ReadOnlySpan<char> Text;
        public int Pos;
        public bool EOF => Pos >= Text.Length;

        public ReadOnlySpan<char> Next()
        {
            int start = Pos;
            int len = Text.Slice(start).IndexOf('\n');
            if (len < 0) len = Text.Length - start;

            Pos = start + len + 1;

            return Text.Slice(start, len);
        }

        // Returns the range of the previously returned line
        public AbsRange GetCurrentRange()
        {
            int start = Text[0..(Pos - 1)].LastIndexOf('\n') + 1;
            return new AbsRange(start, Pos - 1);
        }
    }
}
public class FileCheckResult
{
    public static FileCheckResult Success => new();

    public IReadOnlyList<FileCheckFailure> Failures { get; }

    public bool IsSuccess => Failures.Count == 0;

    public FileCheckResult(IReadOnlyList<FileCheckFailure>? fails = null)
    {
        Failures = fails ?? Array.Empty<FileCheckFailure>();
    }

    public override string ToString() => IsSuccess ? "Success" : $"{Failures.Count} failures";
}
public readonly struct FileCheckFailure
{
    public AbsRange InputPos { get; init; }
    public AbsRange DirectivePos { get; init; }
    public string Message { get; init; }
}