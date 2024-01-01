namespace DistIL.PracticeTests;

using System.Reflection;
using System.Runtime.CompilerServices;

using DistIL.Util;

using Xunit.Sdk;

/// <summary> Runs FileChecker over the IR dump of a test method upon completion. </summary>
internal class CheckCodeGenAfterRunAttribute([CallerFilePath] string sourcePath = "") : BeforeAfterTestAttribute
{
    readonly string _testSourceCode = File.ReadAllText(sourcePath);

    public override void After(MethodInfo methodUnderTest)
    {
        var methodSource = GetMethodBodySource(_testSourceCode, methodUnderTest);
        var dumpSource = File.ReadAllText($"ir_dumps/{methodUnderTest.DeclaringType!.Name}__{methodUnderTest.Name}.txt");

        var result = FileChecker.Check(methodSource.ToString(), dumpSource, StringComparison.Ordinal);
        if (!result.IsSuccess) {
            Assert.Fail(result.Failures[0].Message);
        }
    }

    private static ReadOnlySpan<char> GetMethodBodySource(string source, MethodInfo method)
    {
        int startOffset = source.IndexOf("void " + method.Name + "(");

        Assert.Equal(typeof(void), method.ReturnType);
        Assert.True(startOffset >= 0);

        startOffset = source.LastIndexOf('\n', startOffset); // go back to start of line
        int endOffset = startOffset;

        // Find closing brace
        for (int depth = 0; endOffset < source.Length; endOffset++) {
            char ch = source[endOffset];

            if (ch == '{') depth++;
            if (ch == '}' && --depth == 0) break;
        }
        return source.AsSpan(startOffset, endOffset - startOffset + 1);
    }
}