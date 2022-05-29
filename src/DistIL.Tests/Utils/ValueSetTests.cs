
using DistIL.IR;
using DistIL.Util;

public class ValueSetTests
{
    [Theory]
    [MemberData(nameof(GetDummyValues))]
    public void Test_AddContains(TrackedValue[] values)
    {
        var set = new ValueSet<TrackedValue>();
        int i = 0;

        foreach (var value in values) {
            Assert.True(set.Add(value));
            Assert.True(set.Contains(value));
            Assert.False(set.Add(value));
            Assert.Equal(++i, set.Count);
        }
    }
    [Theory]
    [MemberData(nameof(GetDummyValues))]
    public void Test_Remove(TrackedValue[] values)
    {
        var set = new ValueSet<TrackedValue>();
        foreach (var value in values) {
            Assert.True(set.Add(value));
            Assert.True(set.Remove(value));
            Assert.False(set.Remove(value));
            Assert.True(set.Add(value));
        }
        Assert.Equal(values.Length, set.Count);
    }

    [Theory]
    [MemberData(nameof(GetDummyValues))]
    public void Test_Enumerator(TrackedValue[] values)
    {
        var set = new ValueSet<TrackedValue>();
        foreach (var value in values) {
            Assert.True(set.Add(value));
        }
        CompareSeq(values);

        for (int i = values.Length - 1; i >= 0; i--) {
            set.Remove(values[i]);
            CompareSeq(values[0..i]);
        }

        void CompareSeq(Value[] slice)
        {
            var tmp = new HashSet<Value>();
            foreach (var val in set) {
                tmp.Add(val);
            }
            Assert.Equal(tmp.Count, set.Count);
            tmp.SymmetricExceptWith(slice);
            Assert.Equal(tmp.Count, 0);
        }
    }

    public static IEnumerable<object[]> GetDummyValues()
    {
        yield return new object[] {
            new Value[] {
                new DummyValue(0)
            }
        };
        yield return new object[] { 
            new Value[] {
                new DummyValue(123), new DummyValue(456),
                new DummyValue(789), new DummyValue(101),
            }
        };
    }
}