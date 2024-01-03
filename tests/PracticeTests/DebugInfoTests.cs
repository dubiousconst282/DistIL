namespace DistIL.PracticeTests;

using System.Diagnostics;
using System.Runtime.CompilerServices;

using DistIL.Attributes;

public class DebugInfoTests
{
    [Fact, Optimize]
    public void CheckThatLineNumbersArePreserved()
    {
        int sum = 1;
        var expLine = default((string path, int line));

        try {
            foreach (int x in new[] { 1, 4, 8, 16, 32, 64, 0 }) {
                expLine = GetCallerSourceLocation();
                sum += sum * 100 / x;
            }
            Assert.Fail("Unreachable");
        } catch (DivideByZeroException ex) {
            var frame = new StackTrace(ex, fNeedFileInfo: true).GetFrame(0)!;
            Assert.Equal(expLine.path, frame.GetFileName());
            Assert.Equal(expLine.line, frame.GetFileLineNumber() - 1);
            Assert.Equal(2716770, sum);
        }
    }

    private static (string Path, int Line) GetCallerSourceLocation(
        [CallerFilePath] string path = "",
        [CallerLineNumber] int line = 0
    ) => (path, line);
}