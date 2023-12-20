using BenchmarkDotNet.Attributes;

using DistIL.Attributes;

using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;

[MemoryDiagnoser]
public class ListArrayBuilding
{
    [Params(4, 1024)]
    public int Count {
        set {
            _count = value;
            _items = DynamicList_Gen();
        }
    }

    int _count;
    IReadOnlyList<int> _items;

    [Benchmark]
    public int[] DirectArray_Gen()
    {
        int count = _count;
        var array = new int[count];
        int seed = 12345;

        // Note: JIT doesn't see that `count == array.Length`, won't eliminate bounds check for `i < count`
        for (int i = 0; i < array.Length; i++) {
            seed = (seed * 8121 + 28411) % 134456;
            array[i] = seed;
        }
        return array;
    }

    [Benchmark]
    public int[] DynamicList_Gen()
    {
        int count = _count;
        var list = new List<int>();
        int seed = 12345;

        for (int i = 0; i < count; i++) {
            seed = (seed * 8121 + 28411) % 134456;
            list.Add(seed);
        }
        return list.ToArray();
    }

    [Benchmark]
    public int[] DynamicList_Enumer()
    {
        var list = new List<int>();

        // Note: intentionally downcasting to interface
        foreach (int x in _items) {
            list.Add(x * 3 / 5 + 1);
        }
        return list.ToArray();
    }
}