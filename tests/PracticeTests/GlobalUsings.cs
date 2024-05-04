global using Xunit;

using System.Runtime.CompilerServices;

using DistIL.Attributes;

public static class Utils
{
    [DoNotOptimize, MethodImpl(MethodImplOptions.NoInlining)]
    public static T DoNotOptimize<T>(T value) => value;
}