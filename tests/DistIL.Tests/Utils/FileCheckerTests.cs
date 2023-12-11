namespace DistIL.Tests.Util;

using DistIL.Util;

public class FileCheckerTests
{
    [Fact]
    public void Tokenize()
    {
        var tokens = SplitTokens(" Lorem   ipsum, \tdolor sit amet. \r");
        var expectedTokens = new[] { "Lorem", "ipsum", ",", "dolor", "sit", "amet", "." };

        Assert.Equal(expectedTokens, tokens);
    }

    [Fact]
    public void TokenizeSymbols()
    {
        var tokens = SplitTokens(" $varA=12 {{holeA}} [[holeB:[A-Z]+]] {{badHole} [[]] end");
        var expectedTokens = new[] { "$", "varA", "=", "12", "holeA", "holeB:[A-Z]+", "{", "{", "badHole", "}", "[", "[", "]", "]", "end" };
        Assert.Equal(expectedTokens, tokens);
    }

    private static List<string> SplitTokens(string str)
    {
        var tokens = new List<string>();
        int pos = 0;

        while (true) {
            var token = FileChecker.NextToken(str, ref pos);
            if (token.Type == FileChecker.TokenType.EOF) break;

            tokens.Add(token.Text.ToString());
        }
        return tokens;
    }

    [Fact]
    public void MatchStaticPattern()
    {
        var comp = StringComparison.OrdinalIgnoreCase;

        Assert.True(FileChecker.MatchPattern("HELLO WORLD", "hello world", comp, null));
        Assert.True(FileChecker.MatchPattern("HELLO WORLD", "  hello  world  ", comp, null));
        Assert.True(FileChecker.MatchPattern(" HELLO  WORLD ", "   hello\t   world  ", comp, null));
        Assert.False(FileChecker.MatchPattern("Hello, World", "hello world", comp, null));
        
        Assert.True(FileChecker.MatchPattern("Hello, World.  Lorem \t ipsum,  dolor sit amet.", "lorem ipsum", comp, null));
        Assert.False(FileChecker.MatchPattern("lorem ipsum", "lorem ipsum dolor", comp, null));

        Assert.False(FileChecker.MatchPattern("pineapple", "apple", comp, null));
        Assert.False(FileChecker.MatchPattern("applenut", "apple", comp, null));
        Assert.True(FileChecker.MatchPattern("banana", "banana", comp, null));
    }

    [Fact]
    public void MatchDynamicPattern()
    {
        var comp = StringComparison.OrdinalIgnoreCase;
        var dynEval = new FileChecker.DynamicPatternEvaluator();

        Assert.True(FileChecker.MatchPattern("r1 = call DateTime::get_Now() eol", @"call [[method:\w+::\w+\(\)]] eol", comp, dynEval));
        Assert.False(FileChecker.MatchPattern("r2 = call DateTime::get_UtcNow()", @"call [[method]]", comp, dynEval));
        Assert.True(FileChecker.MatchPattern("r2 = call DateTime::get_Now()eol", @"call [[method]]eol", comp, dynEval));
        Assert.True(FileChecker.MatchPattern("r3 = add 1, 2", @"{{r\d+}} = add", comp, dynEval));
        Assert.False(FileChecker.MatchPattern("r3 = add 1, 2", @"{{\d+}} = add", comp, dynEval));
    }

    [Fact]
    public void BasicChecks()
    {
        Assert.True(CheckICase(
            // Expected
            """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: .ctor
            //CHECK: Bar Qux
            """,

            // Actual
            """
            Line#1
            And then the Foo
            Line#2
            newobj MyClass::.ctor()
            And finally, the  Bar   Qux
            """));

        Assert.False(CheckICase(
            // Expected
            """
            //CHECK: Line#1
            //CHECK: Line#2
            //CHECK: Line#3
            """,

            // Actual
            """
            Line#1
            Line#3
            Line#2
            """));
    }

    [Fact]
    public void CheckSamePass()
    {
        Assert.True(CheckICase(
            // Expected
            """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: Bar Qux
            //CHECK-SAME: the
            Line#3
            //CHECK: end
            """,

            // Actual
            """
            Line#1
            And then the Foo
            Line#2
            And finally, the  Bar   Qux
            Line#3
            END
            """
            ));
    }
    [Fact]
    public void CheckSameFail()
    {
        Assert.False(CheckICase(
            // Expected
            """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: Bar Qux
            //CHECK-SAME: the
            Line#3
            //CHECK: end
            """,

            // Actual
            """
            Line#1
            And then the Foo
            Line#2
            And finally,  Bar   Qux
            Line#3
            END
            """));
    }
    [Fact]
    public void CheckNext()
    {
        Assert.True(CheckICase(
            // Expected
            """
            //CHECK: foo
            //CHECK-NEXT: bar
            //CHECK-NEXT: qux
            //CHECK: end
            """,

            // Actual
            """
            Foo
            Bar
            Qux
            Something else
            End
            """
            ));
    }


    [Fact]
    public void CheckNotSame()
    {
        string expected = """
            // CHECK:       fruits:
            // CHECK-NOT:   apple
            // CHECK-SAME:  banana
            """;
        Assert.True(CheckICase(expected, "fruits: mango, pineapple, banana"));
        Assert.False(CheckICase(expected, "fruits: apple, banana, orange"));
    }

    [Fact]
    public void CheckNotLine()
    {
        string expected = """
            // CHECK:       func1
            // CHECK-NOT:   store
            // CHECK:       ret
            """;
        string actualPass = """
            int func1(int* ptr) {
                r1 = load #ptr -> int
                r2 = add r1, 123 -> int
                r3 = mul r2, 5
                ret r3
            }
            """;
        string actualFail = """
            void func1(int* ptr) {
                r1 = load #ptr -> int
                r2 = add r1, 1 -> int
                store #ptr, r2
                ret
            }
            """;
        Assert.True(CheckICase(expected, actualPass));
        Assert.False(CheckICase(expected, actualFail));
    }

    [Fact]
    public void CheckNotTrailing()
    {
        string expected = """
            // CHECK:       func1
            // CHECK-NOT:   store
            """;
        string actualPass = """
            int func1(int* ptr) {
                r1 = load #ptr -> int
                r2 = add r1, 123 -> int
                r3 = mul r2, 5
                ret r3
            }
            """;
        string actualFail = """
            void func1(int* ptr) {
                r1 = load #ptr -> int
                r2 = add r1, 1 -> int
                store #ptr, r2
                ret
            }
            """;
        Assert.True(CheckICase(expected, actualPass));
        Assert.False(CheckICase(expected, actualFail));
    }

    [Fact]
    public void CheckRegexHole()
    {
        string expected = """
            // CHECK: func1
            // CHECK: add {{\w+}}, {{\d+}}
            // CHECK: ret
            """;
        string actualPass = """
            int func1(int x) {
                r1 = call Math::Abs(int: #x) -> int
                r2 = add r1, 123 -> int
                ret r2
            }
            """;
        string actualFail = """
            int func1(int x) {
                r1 = call Math::Abs(int: #x) -> int
                r2 = add r1, r1 -> int
                ret r2
            }
            """;
        Assert.True(CheckICase(expected, actualPass));
        Assert.False(CheckICase(expected, actualFail));
    }

    [Fact]
    public void CheckSubstHole()
    {
        string expected = """
            // CHECK: func1
            // CHECK: load [[addr:#\w+]]
            // CHECK: store [[addr]]
            // CHECK: ret
            """;
        string actualPass = """
            void func1(int* ptr) {
                r1 = load #ptr -> int
                r2 = add r1, 1 -> int
                store #ptr, r2
                ret
            }
            """;
        string actualFail = """
            void func1(int* src, int* dest) {
                r1 = load #src -> int
                store #dest, r1
                ret
            }
            """;
        Assert.True(CheckICase(expected, actualPass));
        Assert.False(CheckICase(expected, actualFail));
    }

    private static bool CheckICase(string source, string target) => FileChecker.Check(source, target, StringComparison.OrdinalIgnoreCase).IsSuccess;
}