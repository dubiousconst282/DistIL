
using DistIL.IR;
using DistIL.Util;

public class ValueSetTests
{
    [Theory]
    [MemberData(nameof(GetDummyValues))]
    public void Test_AddContains(Value[] values)
    {
        var set = new ValueSet<Value>();
        int i = 0;

        foreach (var value in values) {
            Assert.True(set.Add(value));
            Assert.True(set.Contains(value));
            Assert.False(set.Add(value));
            Assert.Equal(i++, set.Count);
        }
    }
    [Theory]
    [MemberData(nameof(GetDummyValues))]
    public void Test_Remove(Value[] values)
    {
        var set = new ValueSet<Value>();
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
    public void Test_Enumerator(Value[] values)
    {
        var set = new ValueSet<Value>();
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
                ConstInt.CreateI(0)
            }
        };
        yield return new object[] { 
            new Value[] {
                ConstInt.CreateI(123), ConstInt.CreateI(456),
                ConstInt.CreateI(789), ConstInt.CreateI(101),
            }
        };
    }
}