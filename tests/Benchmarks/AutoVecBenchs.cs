using BenchmarkDotNet.Attributes;

using DistIL.Attributes;

using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;

[Optimize(TryVectorize = true), DisassemblyDiagnoser]
public class AutoVecBenchs
{
    [Params(4096)]
    public int ElemCount {
        set {
            var rng = new Random(value - 1);

            _elemCount = value;
            _sourceText = RandStr(value);
            // Allocating arrays is surprisingly expansive, do it once here
            _floatsIn = new float[value];
            _floatsOut = new float[value];
            _intsOut = new int[value];

            for (int i = 0; i < value; i++) {
                _floatsIn[i] = rng.NextSingle() * 2.0f - 1.0f;
            }

            string RandStr(int length)
            {
                var buffer = new byte[length];
                rng.NextBytes(buffer);
                return Convert.ToBase64String(buffer);
            }
        }
    }

    int _elemCount;
    string _sourceText = null!;
    float[] _floatsIn = null!, _floatsOut = null!;
    int[] _intsOut = null!;

    [Benchmark]
    public int CountLetters()
    {
        return _sourceText.Count(ch => ch is >= 'A' and <= 'Z');
    }

    [Benchmark]
    public void GenerateInts()
    {
        var dest = _intsOut;
        int x = _elemCount / 25;

        for (int i = 0; i < dest.Length; i++) {
            dest[i] = Math.Abs(i - 50 * x);
        }
    }

    [Benchmark]
    public void GenerateFloats()
    {
        var dest = _floatsOut;
        _floatsIn.CopyTo(dest, 0);

        for (int i = 0; i < dest.Length; i++) {
            float x = dest[i] + ((i & 7) / 3.5f) - 1.0f;
            dest[i] = x < 0.0f ? 0.0f : x;
        }
    }

    [Benchmark]
    public void NormalizeFloats()
    {
        var dest = _floatsOut;
        _floatsIn.CopyTo(dest, 0);

        float min = float.PositiveInfinity, max = 0.0f;

        for (int i = 0; i < dest.Length; i++) {
            min = Math.Min(min, dest[i]);
            max = Math.Max(max, dest[i]);
        }
        float invMax = 1.0f / max;
        for (int i = 0; i < dest.Length; i++) {
            dest[i] = (dest[i] - min) * invMax;
        }
    }
}