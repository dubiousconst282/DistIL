namespace DistIL.IR.Utils;

using System.Text.RegularExpressions;

/// <summary> Directive-driven file pattern matcher and comparer. </summary>
/// <remarks> Inspired by https://www.llvm.org/docs/CommandGuide/FileCheck.html </remarks>
public class FileChecker
{
    readonly string _source;
    readonly List<Directive> _directives = new();

    static readonly Regex s_DirectiveRegex = new(@"^\s*\/\/\s*CHECK(?:-[A-Z]+)?:.+$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.NonBacktracking);
    static readonly (string K, DirectiveType V)[] s_KnownDirectives = {
        ("CHECK",       DirectiveType.Check),
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
                SourceRange = new AbsRange(end + 1, match.Index + match.Length)
            });
        }
    }

    public bool Check(ReadOnlySpan<char> text, StringComparison compareMode = StringComparison.OrdinalIgnoreCase)
    {
        int directiveIdx = 0;

        foreach (var line in text.EnumerateLines()) {
            bool isMatch = MatchDirective(_directives[directiveIdx], line, compareMode);

            if (isMatch) {
                if (++directiveIdx >= _directives.Count) {
                    return true; //Nothing else to match
                }
            }
        }
        return false;
    }

    private bool MatchDirective(in Directive directive, ReadOnlySpan<char> text, StringComparison compareMode)
    {
        var expectedText = _source.AsSpan().Slice(directive.SourceRange);

        switch (directive.Type) {
            case DirectiveType.Check: {
                return Match(text, expectedText, compareMode);
            }
            default: throw new UnreachableException();
        }
    }

    /// <summary> Checks if <paramref name="text"/> contains <paramref name="pattern"/>, ignoring consecutive whitespace. </summary>
    public static bool Match(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, StringComparison compareMode)
    {
        int textWinPos = 0, patternStartPos = 0;
        NextToken(pattern, ref patternStartPos, out var firstToken);
        Debug.Assert(firstToken.Length > 0, "Pattern cannot be empty");

        while (true) {
            //Use a fast IndexOf() to find the start of a possible match
            int firstTokenOffset = text.Slice(textWinPos).IndexOf(firstToken, compareMode);
            if (firstTokenOffset < 0) return false;

            textWinPos += firstTokenOffset + firstToken.Length;
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
                if (!tokenA.Equals(tokenB, compareMode)) {
                    //No more matches, try again on next window alignment.
                    break;
                }
            }
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
        public AbsRange SourceRange; //Pattern range in source text
    }
    enum DirectiveType
    {
        Invalid,
        Check,
        //TODO: Implement missing essential directives: CHECK-NOT, CHECK-NEXT, CHECK-SAME
    }
}
[Flags]
public enum FileCheckerOptions
{
    Default             = 0,
    IgnoreCase          = 1 << 0,
}