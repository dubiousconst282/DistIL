namespace DistIL.Util;

/// <summary> A de-duplicating stack of object references, where each distinct object is only popped once. </summary>
public class DiscreteStack<T> where T : class
{
    readonly ArrayStack<T> _stack = new();
    readonly RefSet<T> _pushed = new();

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

    public void Clear(bool keepSeen = false)
    {
        if (!keepSeen) {
            _pushed.Clear();
        }
        _stack.Clear();
    }
}