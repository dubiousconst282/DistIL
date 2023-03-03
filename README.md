# DistIL
![GitHub](https://img.shields.io/github/license/dubiousconst282/DistIL)
[![Nuget](https://img.shields.io/nuget/v/DistIL.OptimizerTask)](https://www.nuget.org/packages/DistIL.OptimizerTask)

Post-build IL optimizer and intermediate representation for .NET programs.

# Installation and Usage
The optimizer is distributed as a MSBuild task via NuGet, [DistIL.OptimizerTask](https://www.nuget.org/packages/DistIL.OptimizerTask). It currently only targets .NET 7+ projects.

By default, it will only be invoked in _Release_ mode and transform methods and classes annotated with `[Optimize]`.
It can be enabled globally by setting the `DistilAllMethods` project property to `true`, however that is not recommended because it could lead to unexpected behavior changes.

The IR and infrastructure is provided separately as a standalone library, [DistIL.Core](https://www.nuget.org/packages/DistIL.Core). See the [API walkthrough](./docs/api-walkthrough.md) for details.

# Notable Features
- SSA-based Intermediate Representation
- Linq Expansion
- Lambda Devirtualization
- Method Inlining
- Scalar Replacement of Aggregates

The project is currently stable enough to process libraries such as _ICSharpCode.Decompiler_ and _ImageSharp_ in full without crashing or noticeably changing execution behavior.

## Linq Expansion
Opportunistically rewrites Linq queries into imperative code in bottom-up order. This transform works by pattern matching and can only recognize a predefined set of known calls, which are listed below.

For [typical queries](./tests/Benchmarks/LinqBenchs.cs), it yields speed-ups ranging between 2-8x:

|        Method |         Arguments |         Mean |      Error |     StdDev | Ratio |
|-------------- |------------------ |-------------:|-----------:|-----------:|------:|
| FilterObjects |           Default |     38.72 us |   0.239 us |   0.350 us |  1.00 |
| FilterObjects | /p:RunDistil=true |     15.50 us |   0.126 us |   0.188 us |  0.40 |
|               |                   |              |            |            |       |
|     Aggregate |           Default |    123.32 us |   0.061 us |   0.089 us |  1.00 |
|     Aggregate | /p:RunDistil=true |     15.62 us |   0.122 us |   0.167 us |  0.13 |
|               |                   |              |            |            |       |
|  CountLetters |           Default |     76.94 us |   0.445 us |   0.665 us |  1.00 |
|  CountLetters | /p:RunDistil=true |     12.25 us |   0.027 us |   0.037 us |  0.16 |
|               |                   |              |            |            |       |
| LinqRayTracer |           Default | 33,384.17 us | 394.423 us | 590.354 us |  1.00 |
| LinqRayTracer | /p:RunDistil=true | 22,164.15 us | 269.759 us | 403.762 us |  0.66 |

---

**Supported sources**
  - Special cased: `T[]`, `List<T>`, `string`, `Enumerable.Range()`
  - Fallback to any `IEnumerable<T>`

**Supported stages**
  - `Where`, `Select`, `OfType`, `Cast`
  - `SelectMany`
  - `Skip`

**Supported sinks**
  - `ToList`, `ToArray`, `ToHashSet`, `ToDictionary`
  - `Aggregate`, `Count(predicate)`
  - `First`, `FirstOrDefault`
  - `Any(predicate)`, `All(predicate)`
  - Loop enumeration

**Behavior differences**
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

# Future
Although there is a lot of opportunities for what can be done, the usefulness of this project is still fairly unclear, considering the amount of effort needed to implement most transforms and the relative improvement they could bring.  
The primary focus will be on "aggressive" and "high-level" optimizations, namely:

- Loop and SLP Auto-vectorization
  - It should be as basic and simple as possible, at least initially. For loops, runtime aliasing guards would be a need.
- Partial Redundancy Elimination
  - The goal is to de-duplicate and hoist known "high-level" instructions such as string operations and stateful calls like dictionary lookups, which is apparently not done even by native compilers.  
  This would be an evolution of the current _Value Numbering_ pass, which is incomplete and have some significant issues.