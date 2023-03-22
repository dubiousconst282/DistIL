# DistIL
![GitHub](https://img.shields.io/github/license/dubiousconst282/DistIL)
[![Nuget](https://img.shields.io/nuget/v/DistIL.OptimizerTask)](https://www.nuget.org/packages/DistIL.OptimizerTask)

Post-build IL optimizer and intermediate representation for .NET programs.

# Installation and Usage
The optimizer is distributed as a MSBuild task via NuGet, [DistIL.OptimizerTask](https://www.nuget.org/packages/DistIL.OptimizerTask). It currently only targets .NET 7+ projects.

By default, it will only be invoked in _Release_ mode and transform methods and classes annotated with `[Optimize]`.
It can be enabled globally by setting the `DistilAllMethods` project property to `true`, however that is not recommended because it could lead to unexpected behavior changes.

The IR and infrastructure are provided separately as a standalone library, [DistIL.Core](https://www.nuget.org/packages/DistIL.Core). See the [API walkthrough](./docs/api-walkthrough.md) for details.

# Notable Features
- SSA-based Intermediate Representation
- Linq Expansion
- Loop Vectorization
- Lambda Devirtualization
- Method Inlining
- Scalar Replacement of Aggregates

## Linq Expansion
Opportunistically rewrites Linq queries into imperative code in bottom-up order. This transform works by pattern matching and can only recognize a predefined set of known calls, which are listed below.

For [typical queries](./tests/Benchmarks/LinqBenchs.cs), speed-ups range between 2-10x:
|         Method |         Toolchain |          Mean |       Error |      StdDev | Ratio | RatioSD |
|--------------- |------------------ |--------------:|------------:|------------:|------:|--------:|
|  FilterObjects |          .NET 7.0 |     25.789 μs |   0.9277 μs |   1.0684 μs |  1.00 |    0.00 |
|  FilterObjects | .NET 7.0 + DistIL |     11.986 μs |   0.7977 μs |   0.9187 μs |  0.47 |    0.04 |
|                |                   |               |             |             |       |         |
| FirstPredicate |          .NET 7.0 |     43.959 μs |   1.1397 μs |   1.3124 μs |  1.00 |    0.00 |
| FirstPredicate | .NET 7.0 + DistIL |     12.397 μs |   0.3286 μs |   0.3784 μs |  0.28 |    0.01 |
|                |                   |               |             |             |       |         |
|      Aggregate |          .NET 7.0 |     74.475 μs |   3.6544 μs |   4.2084 μs |  1.00 |    0.00 |
|      Aggregate | .NET 7.0 + DistIL |      5.971 μs |   0.1545 μs |   0.1779 μs |  0.08 |    0.00 |
|                |                   |               |             |             |       |         |
|   CountLetters |          .NET 7.0 |     40.919 μs |   0.9485 μs |   1.0924 μs |  1.00 |    0.00 |
|   CountLetters | .NET 7.0 + DistIL |      3.684 μs |   0.0377 μs |   0.0434 μs |  0.09 |    0.00 |
|                |                   |               |             |             |       |         |
|      RayTracer |          .NET 7.0 | 18,766.715 μs | 493.8189 μs | 568.6825 μs |  1.00 |    0.00 |
|      RayTracer | .NET 7.0 + DistIL | 12,499.714 μs | 373.6395 μs | 430.2838 μs |  0.67 |    0.03 |

---

**Supported sources**
  - Special cased: `T[]`, `List<T>`, `string`, `Enumerable.Range()`
  - Fallback to any `IEnumerable<T>`

**Supported stages**
  - `Where`, `Select`, `OfType`, `Cast`
  - `SelectMany`
  - `Skip`, `Take`

**Supported sinks**
  - `ToList`, `ToArray`, `ToHashSet`, `ToDictionary`
  - `Aggregate`, `Count(predicate)`
  - `First`, `FirstOrDefault`
  - `Any(predicate)`, `All(predicate)`
  - Loop enumeration

**Caveats**
  - `Dispose()` is never called for IEnumerable sources _(not implemented)_
    - Result may leak memory for sources such as `File.ReadLines()`.  
  - Null argument checks are removed
    - Result may throw `NullReferenceException` instead of `ArgumentNullException`.
  - `List<T>` version checks are bypassed
    - Result will never throw `InvalidOperationException` for concurrent modifications. In the worst case where lists are mutated by different threads, this could lead to buffer over-reads and access violations.

### Example
```cs
//Original code
[Optimize]
public float Aggregate()
    => _sourceItems
        .Select(x => x.Weight)
        .Where(x => x > 0.0f && x < 1.0f)
        .Aggregate(0.0f, (r, x) => r + (x < 0.5f ? -x : x));

//Decompiled output
[Optimize]
public float Aggregate()
{
    List<Item> sourceItems = _sourceItems;
    ref Item reference = ref MemoryMarshal.GetArrayDataReference(sourceItems._items);
    ref Item reference2 = ref Unsafe.Add(ref reference, (uint)sourceItems._size);
    float num = 0f;
    for (; Unsafe.IsAddressLessThan(ref reference, ref reference2); reference = ref Unsafe.Add(ref reference, 1))
    {
        float num2 = reference.Weight;
        if (num2 > 0f && num2 < 1f)
        {
            if (num2 < 0.5f) { num2 = 0f - num2; }
            num += num2;
        }
    }
    return num;
}
```

# Loop Vectorization
Prototype loop vectorizer which works on simple for-loops, having no complex branches or instructions.
It is not enabled by default and requires explicit opt-in via `[Optimize(TryVectorize = true)]`.

The impact for [trivial cases](./tests/Benchmarks/AutoVecBenchs.cs) is considerable, and it can even exceed an order of magnitude:
|          Method |         Toolchain |        Mean |       Error |      StdDev | Ratio | Code Size |
|---------------- |------------------ |------------:|------------:|------------:|------:|----------:|
|    CountLetters |          .NET 7.0 | 42,182.7 ns | 1,876.84 ns | 2,161.37 ns |  1.00 |     338 B |
|    CountLetters | .NET 7.0 + DistIL |    463.2 ns |    10.47 ns |    12.06 ns |  0.01 |     178 B |
|                 |                   |             |             |             |       |           |
|    GenerateInts |          .NET 7.0 |  2,039.7 ns |    88.91 ns |    98.83 ns |  1.00 |      80 B |
|    GenerateInts | .NET 7.0 + DistIL |    524.5 ns |    12.61 ns |    14.52 ns |  0.26 |     158 B |
|                 |                   |             |             |             |       |           |
|  GenerateFloats |          .NET 7.0 |  4,617.9 ns |   168.72 ns |   194.30 ns |  1.00 |     462 B |
|  GenerateFloats | .NET 7.0 + DistIL |    896.2 ns |    10.43 ns |    10.71 ns |  0.19 |     563 B |
|                 |                   |             |             |             |       |           |
| NormalizeFloats |          .NET 7.0 |  9,553.2 ns |   194.62 ns |   224.13 ns |  1.00 |     686 B |
| NormalizeFloats | .NET 7.0 + DistIL |  1,020.1 ns |    22.23 ns |    25.60 ns |  0.11 |     819 B |

---

**Supported ops**
- Memory accesses: pointer load/stores (where the address is either an _invariant pointer_ offset by the loop IV `ptr[i]`, or a loop IV itself `*ptr ... ptr++`).
- Arithmetic: `+`, `-`, `*`, `/` (float), `&`, `|`, `^`, `~`
- Math calls: `Abs`, `Min`, `Max`, `Sqrt`, `Floor`, `Ceil`
- Comparisons: any relop if used by `&`, `|`, `^`, `cond ? x : y`, `r += cond`
- Conversions: `float`<->`int`
- Types: any numeric primitive (byte, int, float, etc.)
- If-conversion: transform patterns such as `cond ? x : y`, short `if..else`s, `x && y` into branchless code
- Reductions: `+=`, `*=`, `&=`, `|=`, `^=`, `Min`, `Max`, `+= cond ? 1 : 0`

**Caveats**
- No legality checks: generated code may be wrong in some circumstances (carried dependencies and aliasing)
- No support for mixed types. This is particularly problematic for small int types (byte/short), since they're implicitly widened on arithmetic ops.
- Different behavior for floats and some Math calls:
  - Non-associative float reductions
  - NaNs not propagated in `Min`, `Max`
  - Int `Abs` won't throw on overflow

### Examples
```cs
//Original
[Optimize(TryVectorize = true)]
public static int CountLetters(string text)
    => text.Count(ch => ch is >= 'A' and <= 'Z');

//Decompiled output (reformatted)
[Optimize(TryVectorize = true)]
public static int CountLetters(string text)
{
    ref readonly char reference = ref text.GetPinnableReference();
    ref char reference2 = ref Unsafe.Add(ref reference, text.Length);
    int num = 0;
    for (; (nint)Unsafe.ByteOffset(target: ref reference2, origin: ref reference) >= 32; reference = ref Unsafe.Add(ref reference, 16))
    {
        Vector256<ushort> left = Unsafe.ReadUnaligned<Vector256<ushort>>(ref Unsafe.As<char, byte>(ref reference));
        Vector256<ushort> vector = Vector256.GreaterThanOrEqual(left, Vector256.Create((ushort)65));
        num += BitOperations.PopCount(
            (vector & Vector256.LessThanOrEqual(left, Vector256.Create((ushort)90)))
                .AsByte().ExtractMostSignificantBits()
        ) >>> 1;
    }
    for (; Unsafe.IsAddressLessThan(ref reference, ref reference2); reference = ref Unsafe.Add(ref reference, 1))
    {
        char c = reference;
        num += ((c >= 'A' && c <= 'Z') ? 1 : 0);
    }
    return num;
}
```

Basic loops bounded by array/span length are supported via strength-reduction. Range and assertion propagation via SCCP could help enable support for cases involving multiple buffers safely (TODO).
```cs
//Original
public static void GenerateInts(Span<int> dest, int x) {
    for (int i = 0; i < dest.Length; i++) {
        dest[i] = Math.Abs(i - 50 * x);
    }
}

//Decompiled output (reformatted)
public static void GenerateInts(Span<int> dest, int x)
{
    Span<int> span = dest;
    ref int reference = ref span._reference;
    int length = span.Length;
    int num = length - 7;
    int i;
    for (i = 0; i < num; i += 8)
    {
        ref int reference2 = ref Unsafe.Add(ref reference, i);
        Vector256<int> vector = Vector256.Create(x) * Vector256.Create(50);
        Unsafe.WriteUnaligned(
            ref Unsafe.As<int, byte>(ref reference2), 
            Vector256.Abs(Vector256.Create(i) + Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7) - vector));
    }
    for (; i < length; i++)
    {
        Unsafe.Add(ref reference, i) = Math.Abs(i - x * 50);
    }
}
```

## Method Inlining
Aggressively inline calls for any non-virtual method defined in the same assembly and originally smaller than 32 IL instructions. Recursive inlines are not currently accounted for, and so this may significantly increase the output assembly size and possibly cause performance regressions if the optimizer is enabled globally.

Inlining of methods accessing private members is supported by disabling runtime access checks via `IgnoresAccessChecksToAttribute`, which is undocumented but supported since _.NET Core 1.0_ and by newer versions of _Mono_.

## Scalar Replacement
Removes simple non-escaping object allocations by inlining constituent fields into local variables.  
An object allocation is considered to be non-escaping if all uses throughout the method are from field related instructions.

### Example
```cs
//Original code
[Optimize]
public int GetMagic() => new RandomLCG(123).Next();

public class RandomLCG {
    int _seed;
    public RandomLCG(int seed) => _seed = seed;
    public int Next() => _seed = (_seed * 8121 + 28411) % 134456;
}

//Decompiled output
[Optimize]
public int GetMagic()
{
    return 86102;
}
```
