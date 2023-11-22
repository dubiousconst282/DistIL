namespace DistIL.Tests.Util;

using DistIL.Util;

public class BitSetTests
{
    [Fact]
    public void Test_Everything()
    {
        var bs = new BitSet(130);
        int[] positions = [1, 3, 4, 8, 12, 17, 32, 55, 63, 64, 65, 100, 126];
        foreach (int pos in positions) {
            Assert.False(bs.Contains(pos));
            Assert.True(bs.Add(pos));
            Assert.True(bs.Contains(pos));
            Assert.False(bs.Add(pos));
        }
        Assert.Equal(positions.Length, bs.PopCount());

        int i = 0;
        foreach (int index in bs) {
            Assert.Equal(positions[i++], index);
        }

        Assert.True(bs.ContainsRange(0, 4));
        Assert.True(bs.ContainsRange(33, 56));
        Assert.True(bs.ContainsRange(100, 127));
        Assert.False(bs.ContainsRange(18, 32));
        Assert.False(bs.ContainsRange(33, 55));
        Assert.False(bs.ContainsRange(101, 126));

        bs.Clear();
        Assert.Equal(0, bs.PopCount());

        Assert.False(bs[222]);
        bs[222] = true;
        Assert.True(bs[222]);

        Assert.ThrowsAny<Exception>(() => bs[-1] = true);
    }
}