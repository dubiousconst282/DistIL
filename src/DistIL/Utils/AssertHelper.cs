
namespace DistIL.Util;

using System.Runtime.CompilerServices;

internal static class AssertHelper
{
    /// <summary> Wrapper for <see cref="Debug.Assert(bool)"/>; Checks that <paramref name="cond"/> is true, but only if the code was compiled in debug mode. </summary>
    [Conditional("DEBUG"), DebuggerStepThrough]
    public static void Assert([DoesNotReturnIf(false)] bool cond, [CallerArgumentExpression("cond")] string? msg = null)
    {
        Debug.Assert(cond, msg);
    }

    /// <summary> Throws an <see cref="InvalidOperationException"/> if <paramref name="cond"/> is false. </summary>
    [DebuggerStepThrough]
    public static void Ensure([DoesNotReturnIf(false)] bool cond, [CallerArgumentExpression("cond")] string? msg = null)
    {
        if (!cond) {
            ThrowHelper(msg);
        }
    }

    [DoesNotReturn, StackTraceHidden]
    private static void ThrowHelper(string? msg)
    {
        throw new InvalidOperationException(msg);
    }
}

internal static class Ensure2_TODO
{
    /// <summary> Wrapper for <see cref="Debug.Assert(bool)"/>; Checks that <paramref name="cond"/> is true, but only if the code was compiled in debug mode. </summary>
    [Conditional("DEBUG"), DebuggerStepThrough]
    public static void Assert([DoesNotReturnIf(false)] bool cond, [CallerArgumentExpression("cond")] string? msg = null)
    {
        Debug.Assert(cond, msg);
    }

    /// <summary> Throws an <see cref="InvalidOperationException"/> if <paramref name="cond"/> is false. </summary>
    [DebuggerStepThrough]
    public static void That([DoesNotReturnIf(false)] bool cond, [CallerArgumentExpression("cond")] string? msg = null)
    {
        if (!cond) {
            ThrowHelper(msg);
        }

        [DoesNotReturn, StackTraceHidden]
        static void ThrowHelper(string? msg)
            => throw new InvalidOperationException(msg);
    }

    [DebuggerStepThrough]
    public static void ValidIndex(int index, int length, string? msg = null)
    {
        Assert(length >= 0);

        if ((uint)index >= (uint)length) {
            ThrowHelper(msg);
        }

        [DoesNotReturn, StackTraceHidden]
        static void ThrowHelper(string? msg)
            => throw new IndexOutOfRangeException(msg);
    }
}