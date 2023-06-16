namespace DistIL.Tests.Util;

using DistIL.IR.Utils;

public class FileCheckerTests
{
    [Fact]
    public void Tokenize()
    {
        var tokens = new List<string>();
        int pos = 0;

        while (FileChecker.NextToken("Hello, World. Lorem ipsum, dolor sit amet.", ref pos, out var token)) {
            tokens.Add(token.ToString());
        }

        var expectedTokens = new[] { "Hello", ",", "World", ".", "Lorem", "ipsum", ",", "dolor", "sit", "amet", "." };
        Assert.Equal(expectedTokens, tokens);
    }


    [Fact]
    public void LineMatch()
    {
        Assert.True(FileChecker.Match("HELLO WORLD", "hello world", StringComparison.OrdinalIgnoreCase));
        Assert.True(FileChecker.Match("HELLO WORLD", "  hello  world  ", StringComparison.OrdinalIgnoreCase));
        Assert.True(FileChecker.Match(" HELLO  WORLD ", "   hello\t   world  ", StringComparison.OrdinalIgnoreCase));
        Assert.False(FileChecker.Match("Hello, World", "hello world", StringComparison.OrdinalIgnoreCase));

        Assert.True(FileChecker.Match("Hello, World!  Lorem \t ipsum,  dolor sit amet.", "lorem ipsum", StringComparison.OrdinalIgnoreCase));
        Assert.False(FileChecker.Match("lorem ipsum", "lorem ipsum dolor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BasicChecks()
    {
        string expected = """
            Line#1
            //CHECK: Foo
            Line#2
            //CHECK: Bar Kux
            """;
        string actual = """
            Line#1
            And then the Foo
            Line#2
            And finally, the  Bar   Kux
            """;
        var checker = new FileChecker(expected);

        Assert.True(checker.Check(actual));
    }
}