namespace DistIL.Util;

using System.Runtime.CompilerServices;

/// <summary> Stack backed by array that allows byref accesses. (Also unsafer) </summary>
public class ArrayStack<T>
{
    T[] _arr;
    int _head;

    public ArrayStack(int initialCapacity = 4)
    {
        _arr = new T[initialCapacity];
    }

    /// <summary> Returns a ref to the top of the stack, or throws an exception if the stack is empty. </summary>
    public ref T Top => ref _arr[_head - 1];

    public int Count => _head;
    public bool IsEmpty => _head == 0;

    public ref T this[int index] {
        get {
            Assert(index >= 0 && index < _head);
            return ref _arr[index];
        }
    }

    /// <summary>
    /// Removes and returns a reference to the top of the stack, 
    /// which will remain valid until the stack is modified again. 
    /// An exception is thrown if the stack is empty. 
    /// </summary>
    public ref T Pop() => ref _arr[--_head];
    public void Push(T value) => PushRef() = value;

    /// <summary>
    /// Allocates a new element on the top of the stack and returns its reference, 
    /// which will remain valid until the stack is modified again.
    /// </summary>
    public ref T PushRef()
    {
        if (_head >= _arr.Length) {
            Array.Resize(ref _arr, _arr.Length * 2);
        }
        return ref _arr[_head++];
    }

    /// <summary>
    /// If the stack is not empty, copies the value on top of the stack to `value` and return true; 
    /// otherwise, leaves `value` uninitialized and return false. 
    /// </summary>
    public bool TryPop(out T value)
    {
        if (_head > 0) {
            value = _arr[--_head];
            return true;
        }
        Unsafe.SkipInit(out value);
        return false;
    }

    public Span<T>.Enumerator GetEnumerator() => _arr.AsSpan(0, _head).GetEnumerator();
}