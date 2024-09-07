namespace DistIL.Util;

/// <summary> A de-duplicating stack of object references, where each distinct object is only popped once. </summary>
public class DiscreteStack<T> where T : class
{
    readonly ArrayStack<T> _stack = new();
    readonly RefSet<T> _pushed = new();

    /// <summary> Number of items currently in the stack. </summary>
    public int Depth => _stack.Count;

    /// <summary> Number of distinct items ever pushed to the stack. </summary>
    public int UniqueCount => _pushed.Count;

    /// <inheritdoc cref="ArrayStack{T}.Top"/>
    public ref T Top => ref _stack.Top;

    public DiscreteStack() { }
    public DiscreteStack(T head) => Push(head);

    public void Push(T value)
    {
        if (_pushed.Add(value)) {
            _stack.Push(value);
        }
    }

    public bool TryPop(out T item) => _stack.TryPop(out item);

    public bool WasPushed(T item) => _pushed.Contains(item);
    public void UnmarkPushed(T item) => _pushed.Remove(item);

    public Iterator<T> EnumeratePushed() => _pushed.GetEnumerator();

    public void Clear(bool rememberPushed = false)
    {
        if (!rememberPushed) {
            _pushed.Clear();
        }
        _stack.Clear();
    }
}