namespace DistIL.PracticeTests;

using DistIL.Attributes;

[Optimize, CheckCodeGenAfterRun]
public class ExpandLinqTests
{
    [Fact]
    public void Array_Map_PredCount()
    {
        int[] source = [
            79, 133, 11, 155, 151, 190, 3, 24, 196, 118, 97, 67, 40, 62, 18, 179,
            136, 81, 73, 15, 31, 242, 47, 1, 58, 50, 123, 89, 203, 52, 54, 92
        ];
        int count = source.Select(x => x - 128).Count(x => x > 0 && x < 64);
        Assert.Equal(6, count);

        // CHECK-NOT: call Enumerable
    }

    [Fact]
    public void Range_Map_ToList()
    {
        var result = Enumerable.Range(0, 13).Select(x => x * 2).ToList();

        Assert.Equal([0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24], result);

        // CHECK-NOT: call Enumerable
        // CHECK-NOT: Add(
    }

    [Fact]
    public void Array_CastFilterMap_ToArray()
    {
        object[] source = [
            "2:dolor", 9365, "11:tempor", "13:ut", "9:do", 9525, 6433, 4785, 8846,
            "3:sit", "15:et", "1:ipsum", "10:eiusmod", 7298, 4809, "0:lorem",
            "14:labore", "8:sed", "4:amet", "5:consectetur", "7:elit", "12:incididunt",
            8983, "6:adipiscing", 7882, 2343, 6152, 3178, 5586, 1282, 7126, 9023
        ];
        string[] expected = "dolor tempor ut do sit et ipsum eiusmod lorem labore sed amet consectetur elit incididunt adipiscing".Split(' ');

        var results = source
            .OfType<string>()
            .Where(s => s.Length > 0)
            .Select(s => s.Split(':')[1])
            .ToArray();

        Assert.Equal(expected, results);

        // CHECK-NOT: call Enumerable
    }
    
    [Fact]
    public void Range_Aggregate()
    {
        var result = Enumerable.Range(1, 50).Select(x => x * x).Aggregate(0, (r, x) => r + x);
        Assert.Equal(42925, result);

        // CHECK-NOT: call Enumerable
    }

    [Fact]
    public void String_Count()
    {
        string text = "The Quick Brown Fox Jumped Over The Lazy Dog";
        int upperLetters = text.Count(ch => ch is >= 'A' and <= 'Z');
        Assert.Equal(9, upperLetters);

        // CHECK-NOT: call Enumerable
    }

    [Fact]
    public void Array_TakeAggregate_SideEffect()
    {
        string[] arr = ["100", "28", "not a number"];

        int result = arr.Select(s => int.Parse(s)).Take(2).Aggregate(0, (r, x) => r + x);

        Assert.Equal(128, result);

        // CHECK-NOT: call Enumerable
    }
    
    [Fact]
    public void Disposeable_Enumerable_Count()
    {
        var source = "The Quick Brown Fox Jumped Over The Lazy Dog".Split(' ').AsEnumerable();
        int result = source.Count(s => !s.Contains('x'));

        Assert.Equal(8, result);

        // CHECK: try finally
        // CHECK-NOT: call Enumerable
        // CHECK: callvirt IDisposable::Dispose
    }

    [Fact]
    public void StructEnumerable_Count()
    {
        var source = new ArraySegment<string>("The Quick Brown Fox Jumped Over The Lazy Dog".Split(' '));
        int result = source.Count(s => !s.Contains('x'));

        Assert.Equal(8, result);

        // CHECK: GetEnumerator
        // CHECK-SAME: ArraySegment`1+Enumerator[string]
        // CHECK: try finally
    }
}