namespace DistIL.PracticeTests;

using DistIL.Attributes;

[Optimize]
public class EhRegionTests
{
    [Theory, InlineData(0), InlineData(1)]
    [CheckCodeGenAfterRun]
    public void MultiExit_Finally1(int path)
    {
        int x = path;

        try {
            try {
                Utils.DoNotOptimize(x); // fake side effect

                if (x > 0) {
                    goto Target1;
                } else {
                    goto Target2;
                }
            } finally {
                x += 10;
            }
        Target1:
            x += 5;
        Target2:
            x += 2;
        } finally {
            Assert.Equal(path == 0 ? 12 : 18, x);
        }

        // CHECK: try finally
        // CHECK: leave [[target1:.*]]
        // CHECK: leave [[target2:.*]]
        // CHECK: resume [[target1]], [[target2]]
    }

    [Fact]
    public void CrossingVars_Catch1()
    {
        int x = 1;
        string str = "1;0";

        try {
            int y = int.Parse(str.Split(';')[1]);
            x = 2;
            x = 100 / y;
        } catch (DivideByZeroException) {
            Assert.Equal(2, x);
            x = 400;
        }
        Assert.Equal(400, x);
    }

    [Fact]
    public void CrossingVars_Finally1()
    {
        int x = 1;
        string str = "123";

        try {
            x = int.Parse(str) * 1000;
        } finally {
            x *= 2;
        }
        Assert.Equal(123 * 1000 * 2, x);
    }
}