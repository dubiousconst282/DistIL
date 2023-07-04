namespace DistIL.Tests.Util;

using DistIL.Util;

public class FileCheckerTests
{
    [Fact]
    public void Tokenize()
    {
        var tokens = new List<string>();
        int pos = 0;

        while (FileChecker.NextToken(" Hello, World.  Lorem   ipsum, \tdolor sit amet. \r", ref pos, out var token)) {
            tokens.Add(token.ToString());
        }

        var expectedTokens = new[] { "Hello", ",", "World", ".", "Lorem", "ipsum", ",", "dolor", "sit", "amet", "." };
        Assert.Equal(expectedTokens, tokens);
    }

    [Fact]
    public void MatchPatternLine()
    {
        var ignoreCase = StringComparison.OrdinalIgnoreCase;

        Assert.True(FileChecker.MatchPattern("HELLO WORLD", "hello world", ignoreCase));
        Assert.True(FileChecker.MatchPattern("HELLO WORLD", "  hello  world  ", ignoreCase));
        Assert.True(FileChecker.MatchPattern(" HELLO  WORLD ", "   hello\t   world  ", ignoreCase));
        Assert.False(FileChecker.MatchPattern("Hello, World", "hello world", ignoreCase));
        
        Assert.True(FileChecker.MatchPattern("Hello, World.  Lorem \t ipsum,  dolor sit amet.", "lorem ipsum", ignoreCase));
        Assert.False(FileChecker.MatchPattern("lorem ipsum", "lorem ipsum dolor", ignoreCase));

        Assert.False(FileChecker.MatchPattern("pineapple", "apple", ignoreCase));
        Assert.False(FileChecker.MatchPattern("applenut", "apple", ignoreCase));
        Assert.True(FileChecker.MatchPattern("banana", "banana", ignoreCase));
    }

    [Fact]
    public void BasicChecks()
    {
        Assert.True(CheckICase(
            //Expected
            """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: Bar Qux
            """,

            //Actual
            """
            Line#1
            And then the Foo
            Line#2
            And finally, the  Bar   Qux
            """));

        Assert.False(CheckICase(
            //Expected
            """
            //CHECK: Line#1
            //CHECK: Line#2
            //CHECK: Line#3
            """,

            //Actual
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
            //Expected
            """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: Bar Qux
            //CHECK-SAME: the
            Line#3
            //CHECK: end
            """,

            //Actual
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
            //Expected
            """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: Bar Qux
            //CHECK-SAME: the
            Line#3
            //CHECK: end
            """,

            //Actual
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
            //Expected
            """
            //CHECK: foo
            //CHECK-NEXT: bar
            //CHECK-NEXT: qux
            //CHECK: end
            """,

            //Actual
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

    private static bool CheckICase(string source, string target) => FileChecker.Check(source, target, StringComparison.OrdinalIgnoreCase).IsSuccess;
}