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

    public static FileCheckResult Check(string source, ReadOnlySpan<char> target, StringComparison compMode)
    {
        return new FileChecker(source).Check(target, compMode);
    }

    public FileCheckResult Check(ReadOnlySpan<char> text, StringComparison compMode)
    {
        return Check(new LineReader() { Text = text, Pos = 0 }, compMode);
    }

    private FileCheckResult Check(LineReader lines, StringComparison compMode)
    {
        const int NullPos = int.MaxValue;
        var dirs = _directives;
        var dynEval = _hasDynamicPatterns ? new DynamicPatternEvaluator() : null;
        int pos = 0, notDirStartPos = NullPos;
        var currDir = default(Directive);

        Advance();

        while (true) {
            if (lines.EOF || currDir.Type == DirectiveType.Invalid) {
                //Only succeed if there are no more directives or the last one is CHECK-NOT
                if (currDir.Type is DirectiveType.Invalid or DirectiveType.CheckNot) {
                    return FileCheckResult.Success;
                }
                goto Fail;
            }
            Debug.Assert(currDir.Type is DirectiveType.Check or DirectiveType.CheckNot);

            var currLine = lines.Next();
            if (MatchExclusions(currLine)) goto Fail;

            if (currDir.Type == DirectiveType.Check) {
                if (!Match(currLine, checkExclusions: false)) continue;

                while (currDir.Type == DirectiveType.CheckSame) {
                    if (!Match(currLine)) goto Fail;
                }
                while (currDir.Type == DirectiveType.CheckNext) {
                    if (lines.EOF || !Match(lines.Next())) goto Fail;
                }
            }
        }
    Fail:
        int lineEnd = lines.Pos - 1;
        int lineStart = lines.Text.Slice(0, lineEnd).LastIndexOf('\n') + 1;
        return new FileCheckResult(new AbsRange(lineStart, lineEnd), pos - 1);

        bool Match(ReadOnlySpan<char> line, bool checkExclusions = true)
        {
            if (checkExclusions && MatchExclusions(line)) {
                return false;
            }
            if (MatchDir(line, currDir)) {
                Advance();
                return true;
            }
            return false;
        }
        //Checks if line matches any of the preceeding CHECK-NOT directives, if any
        bool MatchExclusions(ReadOnlySpan<char> line)
        {
            bool trail = currDir.Type == DirectiveType.CheckNot; //last directive is CHECK-NOT

            for (int i = notDirStartPos; i < pos - (trail ? 0 : 1); i++) {
                Debug.Assert(dirs[i].Type == DirectiveType.CheckNot);
                if (MatchDir(line, dirs[i])) return true;
            }
            return false;
        }
        bool MatchDir(ReadOnlySpan<char> line, in Directive dir)
        {
            var pattern = _source.AsSpan().Slice(dir.PatternRange);
            return MatchPattern(line, pattern, compMode, dynEval);
        }
        void Advance()
        {
            notDirStartPos = NullPos;
            currDir.Type = DirectiveType.Invalid;

            while (pos < dirs.Count) {
                currDir = dirs[pos++];

                if (currDir.Type != DirectiveType.CheckNot) break;
                notDirStartPos = Math.Min(notDirStartPos, pos - 1);
            }
        }
    }

    //TODO: this looks more complicated than it needs to be

    /// <summary> Checks if <paramref name="text"/> matches the given pattern. </summary>
    public static bool MatchPattern(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, StringComparison compMode, DynamicPatternEvaluator? dynEval)
    {
        int textWinPos = 0, patternStartPos = 0;
        NextToken(pattern, ref patternStartPos, out var firstToken);
        Debug.Assert(firstToken.Length > 0, "Pattern cannot be empty");
        Debug.Assert(!IsHoleToken(firstToken)); //FIXME

        while (true) {
            //Use a fast IndexOf() to find the start of a possible match
            int firstTokenOffset = IndexOfToken(text, firstToken, textWinPos, compMode);
            if (firstTokenOffset < 0) return false;

            textWinPos = firstTokenOffset + firstToken.Length;
            int textPos = textWinPos;
            int patternPos = patternStartPos;

            //Check for matching tokens
            while (true) {
                bool gotA = NextToken(pattern, ref patternPos, out var tokenA);

                //Handle regex/substitution holes
                if (gotA && IsHoleToken(tokenA) && dynEval != null) {
                    var prefix = text.Slice(textPos).TrimStart();
                    var matchSubRange = dynEval.Match(tokenA, prefix, compMode);
                    if (matchSubRange.IsEmpty) break;

                    int trimLen = text.Length - textPos - prefix.Length;
                    textPos += matchSubRange.End + trimLen;
                    continue;
                }
                bool gotB = NextToken(text, ref textPos, out var tokenB);

                if (!gotA || !gotB) {
                    //Consider a match only if we have no more pattern tokens.
                    return !gotA;
                }
                if (!tokenA.Equals(tokenB, compMode)) {
                    //No more matches, try again on next window alignment.
                    break;
                }
            }
        }
    }

    private static int IndexOfToken(ReadOnlySpan<char> text, ReadOnlySpan<char> token, int startOffset, StringComparison compMode)
    {
        while (true) {
            int offset = startOffset + text.Slice(startOffset).IndexOf(token, compMode);

            if (offset < startOffset) {
                return -1;
            }
            //Make sure that's a full token, not just an affix
            if ((offset <= 0 || IsTokenSeparator(text[offset - 1])) &&
                (offset + token.Length >= text.Length || IsTokenSeparator(text[offset + token.Length]))
            ) {
                return offset;
            }
            startOffset = offset + token.Length;
        }
    }

    internal static bool NextToken(ReadOnlySpan<char> text, scoped ref int pos, out ReadOnlySpan<char> token)
    {
        int start = pos;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;

        int end = start;

        //Holes: {{regex}} or [[var]]
        if (start + 1 < text.Length && text[start] is '{' or '[' && text[start] == text[start + 1]) {
            string closer = text[start] == '{' ? "}}" : "]]";
            int closerDist = text.Slice(start + 2).IndexOf(closer, StringComparison.Ordinal);

            if (closerDist > 0) {
                end += closerDist + 4;
                goto Found;
            }
            //Backtrack if terminator is not found
            end = start;
        }

        //Normal token: [A-Z0-9_]+|.
        while (end < text.Length && !IsTokenSeparator(text[end])) end++;

        //Ensure we never output empty tokens; unknown chars are individual tokens.
        if (start == end && end < text.Length) end++;

    Found:
        token = text[start..end];
        pos = end;

        return start != end;
    }

    private static bool IsTokenSeparator(char ch) => !char.IsAsciiLetterOrDigit(ch) && ch != '_';

    private static bool IsHoleToken(ReadOnlySpan<char> str)
    {
        return str is ['{', '{', .., '}', '}'] or
                      ['[', '[', .., ']', ']'];
    }

    public class DynamicPatternEvaluator
    {
        readonly Dictionary<string, Regex> _regexCache = new();
        readonly Dictionary<string, string> _substMap = new();

        public AbsRange Match(ReadOnlySpan<char> hole, ReadOnlySpan<char> text, StringComparison compMode)
        {
            if (hole[0] == '{') {
                return MatchRegex(hole[2..^2], text, compMode);
            }
            //Substitution hole
            Debug.Assert(hole[0] == '[');
            int colonIdx = hole.IndexOf(':');

            if (colonIdx < 0) {
                //Use: [[key]]  
                //TODO: this should probably match tokens instead of substr via MatchPattern()
                string key = hole[2..^2].ToString();

                return _substMap.TryGetValue(key, out string? val) && text.StartsWith(val, compMode) 
                        ? new AbsRange(0, text.Length - text.Length + val.Length) : default;
            } else {
                //Assignment: [[key:pattern]]
                var range = MatchRegex(hole[(colonIdx + 1)..^2], text, compMode);
                if (!range.IsEmpty) {
                    string key = hole[2..colonIdx].ToString();
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
    }
}
public readonly struct FileCheckResult
{
    public static FileCheckResult Success => new(default, -1);

    /// <summary> Approximate position of the text that didn't match the directive. </summary>
    public AbsRange TextRange { get; }

    /// <summary> Offset of the directive in the source string. </summary>
    public int DirectivePos { get; }

    public bool IsSuccess => DirectivePos < 0;

    public FileCheckResult(AbsRange textRange, int directivePos)
    {
        TextRange = textRange;
        DirectivePos = directivePos;
    }

    public override string ToString() => IsSuccess ? "Success" : $"Fail #{DirectivePos} at {TextRange}";

    public enum ErrorReason { }
}