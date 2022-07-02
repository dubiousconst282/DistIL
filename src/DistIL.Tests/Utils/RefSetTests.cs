using DistIL.Util;

public class RefSetTests
{
    [Theory, MemberData(nameof(GetData))]
    public void Test_InsertRemove(Item[] values)
    {
        var set = new RefSet<Item>();
        int i = 0;

        foreach (var value in values) {
            Assert.Equal(i, set.Count);
            Assert.False(set.Contains(value));

            Assert.True(set.Add(value));
            Assert.True(set.Contains(value));
            Assert.Equal(i + 1, set.Count);
            Assert.False(set.Add(value));

            Assert.True(set.Remove(value));
            Assert.False(set.Contains(value));
            Assert.Equal(i, set.Count);

            Assert.True(set.Add(value));
            Assert.False(set.Add(value));
            i++;
        }
        set.Clear();
        Assert.Equal(0, set.Count);
        Assert.False(set.GetEnumerator().MoveNext());
    }

    [Theory, MemberData(nameof(GetData))]
    public void Test_Enumerator(Item[] values)
    {
        var set = new RefSet<Item>();
        foreach (var value in values) {
            Assert.True(set.Add(value));
        }
        CompareSeq(values);

        for (int i = values.Length - 1; i >= 0; i--) {
            set.Remove(values[i]);
            CompareSeq(values[0..i]);
        }

        void CompareSeq(Item[] slice)
        {
            var tmp = new HashSet<Item>(ReferenceEqualityComparer.Instance);
            foreach (var val in set) {
                tmp.Add(val);
            }
            Assert.Equal(tmp.Count, set.Count);
            tmp.SymmetricExceptWith(slice);
            Assert.Empty(tmp);
        }
    }

    public static IEnumerable<object[]> GetData()
    {
        foreach (int len in new[] { 1, 2, 3, 4, 7 }) {
            var items = new Item[len];

            for (int j = 0; j < len; j++) {
                items[j] = new Item(len * 1000 + j);
            }
            yield return new object[] { items };
        }
    }

    public record Item(int Id);
}