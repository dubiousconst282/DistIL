namespace DistIL.PracticeTests;

using DistIL.Attributes;

[Optimize]
public class RegAllocTests
{
    [Fact]
    public void LiveReg_Loop_Finally()
    {
        long a = 0, b = 0, c = 0;

        for (int i = 0; i < 100 && c < 100; i++, c++) {
            try {
                a += i;
            } finally {
                int tmp = (int)(a % 5);  // buggy regalloc will reuse `i`
                if (tmp == 1 || tmp == 3) {
                    b += a;
                }
            }
        }
        Assert.Equal(4950, a);
        Assert.Equal(99950, b);
    }
}