namespace DistIL.Util;

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public unsafe class MemUtils
{
    /// <summary> Reads a T value from the span. Does not perform bounds check in release mode. </summary>
    public static T Read<T>(ReadOnlySpan<byte> buf, int pos) where T : unmanaged
    {
        Assert((uint)(pos + sizeof(T)) < (uint)buf.Length);
        return Unsafe.ReadUnaligned<T>(ref Unsafe.AddByteOffset(ref MemoryMarshal.GetReference(buf), (IntPtr)pos));
    }

    /// <summary> Reverse bytes of a primitive value. `T` must be a blittable type of size `1, 2, 4, or 8`. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T BSwap<T>(T value) where T : unmanaged
    {
        static V I<V>(T v) => Unsafe.As<T, V>(ref v);
        static T O<V>(V v) => Unsafe.As<V, T>(ref v);

        if (sizeof(T) == 1) return value;
        if (sizeof(T) == 2) return O(BinaryPrimitives.ReverseEndianness(I<ushort>(value)));
        if (sizeof(T) == 4) return O(BinaryPrimitives.ReverseEndianness(I<uint>(value)));
        if (sizeof(T) == 8) return O(BinaryPrimitives.ReverseEndianness(I<ulong>(value)));

        throw new NotSupportedException();
    }
}

public ref struct SpanReader
{
    public readonly ReadOnlySpan<byte> Span;
    public int Offset;
    public int Length => Span.Length;

    public SpanReader(ReadOnlySpan<byte> span)
    {
        Span = span;
        Offset = 0;
    }

    public byte ReadByte() => Span[Offset++];

    public unsafe T ReadLE<T>() where T : unmanaged
    {
        T value = Read<T>();
        return BitConverter.IsLittleEndian ? value : MemUtils.BSwap(value);
    }
    public unsafe T ReadBE<T>() where T : unmanaged
    {
        T value = Read<T>();
        return BitConverter.IsLittleEndian ? MemUtils.BSwap(value) : value;
    }

    private unsafe T Read<T>() where T : unmanaged
    {
        if (Offset + sizeof(T) >= Span.Length) {
            ThrowEOS();
        }
        T value = MemUtils.Read<T>(Span, Offset);
        Offset += sizeof(T);
        return value;
    }

    private void ThrowEOS()
    {
        throw new InvalidOperationException("Cannot read past span");
    }
}