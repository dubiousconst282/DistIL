namespace DistIL.Util;

using System.Text.RegularExpressions;

/// <summary> Directive-driven file pattern matcher and comparer. Inspired by https://www.llvm.org/docs/CommandGuide/FileCheck.html </summary>
public class FileChecker
{
    readonly string _source;
    readonly List<Directive> _directives = new();

    static readonly Regex s_DirectiveRegex = new(@"^\s*\/\/\s*CHECK(?:-[A-Z]+)?:.+$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.NonBacktracking);
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
            int start = source.IndexOf("CHECK", match.Index, StringComparison.OrdinalIgnoreCase);
            int end = source.IndexOf(':', start);
            var type = s_KnownDirectives.FirstOrDefault(d => source.AsSpan()[start..end].EqualsIgnoreCase(d.K)).V;

            if (type == DirectiveType.Invalid) {
                int lineNo = source.Take(match.Index).Count(c => c == '\n');
                throw new FormatException($"Unknown FileCheck directive '{source[start..end]}' on line {lineNo}");
            }
            _directives.Add(new() {
                Type = type,
                PatternRange = new AbsRange(end + 1, match.Index + match.Length),
                //TODO: support {{regex}} and [[substitution]] holes
            });
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
        int pos = 0, notDirStartPos = NullPos;
        bool hasPrevMatchLoop = false;
        Directive currDir = default;
        ReadOnlySpan<char> currLine = "";

        AdvanceDir();

        while (true) {
            if (currDir.Type != DirectiveType.CheckSame) {
                if (lines.EOF) break;
                currLine = lines.Next();
            }

            //Check exclusion directives, if any
            if (notDirStartPos != NullPos) {
                bool trail = currDir.Type == DirectiveType.CheckNot; //last directive is CHECK-NOT

                for (int i = notDirStartPos; i < pos - (trail ? 0 : 1); i++) {
                    Debug.Assert(dirs[i].Type == DirectiveType.CheckNot);
                    if (Match(currLine, dirs[i])) goto Fail;
                }
                if (trail) continue;
            }

            //Check for an actual match
            bool isMatch = Match(currLine, currDir);
            bool hasPrevMatch = hasPrevMatchLoop;
            hasPrevMatchLoop = isMatch;

            //If there is no match, skip to the next line or fail depending on directive
            if (!isMatch) {
                if (currDir.Type == DirectiveType.Check) continue;
                if (currDir.Type == DirectiveType.CheckSame) goto Fail;
                if (currDir.Type == DirectiveType.CheckNext && !hasPrevMatch) goto Fail;
            }

            //There is a match, skip to the next directive.
            //If there are no more and the last is not CHECK-AND, end the loop.
            if (!AdvanceDir() && notDirStartPos == NullPos) break;
        }
        if (lines.EOF || !AdvanceDir()) {
            return FileCheckResult.Success;
        }
    Fail:
        int lineEnd = lines.Pos - 1;
        int lineStart = lines.Text.Slice(0, lineEnd).LastIndexOf('\n') + 1;
        return new FileCheckResult(new AbsRange(lineStart, lineEnd), pos - 1);

        bool Match(ReadOnlySpan<char> line, in Directive dir)
        {
            var pattern = _source.AsSpan().Slice(dir.PatternRange);
            return MatchPattern(line, pattern, compMode);
        }
        bool AdvanceDir()
        {
            int startPos = pos;

            while (pos < dirs.Count) {
                currDir = dirs[pos++];

                if (currDir.Type != DirectiveType.CheckNot) {
                    notDirStartPos = pos == startPos + 1 ? NullPos : startPos;
                    return true;
                }
            }
            notDirStartPos = startPos < dirs.Count ? startPos : NullPos;
            return false;
        }
    }

    /// <summary> Checks if <paramref name="text"/> contains <paramref name="pattern"/>, ignoring consecutive whitespace. </summary>
    public static bool MatchPattern(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, StringComparison compMode)
    {
        int textWinPos = 0, patternStartPos = 0;
        NextToken(pattern, ref patternStartPos, out var firstToken);
        Debug.Assert(firstToken.Length > 0, "Pattern cannot be empty");

        while (true) {
            //Use a fast IndexOf() to find the start of a possible match
            int firstTokenOffset = IndexOfToken(text, firstToken, textWinPos, compMode);
            if (firstTokenOffset < 0) return false;

            textWinPos = firstTokenOffset + firstToken.Length;
            int textPos = textWinPos;
            int patternPos = patternStartPos;

            //Check for matching tokens
            while (true) {
                bool gotA = NextToken(text, ref textPos, out var tokenA);
                bool gotB = NextToken(pattern, ref patternPos, out var tokenB);

                if (!gotA || !gotB) {
                    //Consider a match only if we have no more pattern tokens.
                    return !gotB;
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
        while (end < text.Length && (!IsTokenSeparator(text[end]) || end == start)) end++;

        token = text[start..end];
        pos = end;

        return start != end;
    }

    private static bool IsTokenSeparator(char ch) => !char.IsAsciiLetterOrDigit(ch) && ch != '_';

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
    public static FileCheckResult Success => new();

    /// <summary> Approximate position of the text that didn't match the directive. </summary>
    public AbsRange TextRange { get; }

    /// <summary> Offset of the directive in the source string. </summary>
    public int DirectivePos { get; }

    public bool IsSuccess => TextRange.IsEmpty;

    public FileCheckResult(AbsRange textRange, int directivePos)
    {
        TextRange = textRange;
        DirectivePos = directivePos;
    }

    public override string ToString() => IsSuccess ? "Success" : $"Fail #{DirectivePos} at {TextRange}";

    public enum ErrorReason { }
}