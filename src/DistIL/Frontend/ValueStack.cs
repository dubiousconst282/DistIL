namespace DistIL.Frontend;

using DistIL.IR;

internal class ValueStack : IEnumerable<Value>
{
    private Value[] _entries;
    private int _head;

    public int Count => _head;
    public Value this[int index] {
        get => _entries[index];
        set => _entries[index] = value;
    }

    public ValueStack(int capacity)
    {
        _entries = new Value[capacity];
        _head = 0;
    }

    public void Push(Value value)
    {
        if (_head < _entries.Length) {
            _entries[_head++] = value;
        } else {
            throw new InvalidProgramException("Stack overflow");
        }
    }
    public Value Pop()
    {
        if (--_head >= 0) {
            return _entries[_head];
        } else {
            throw new InvalidProgramException("Stack underflow");
        }
    }

    public ValueStack Clone()
    {
        var ns = new ValueStack(_entries.Length);
        _entries.CopyTo(ns._entries, 0);
        ns._head = _head;
        return ns;
    }

    public IEnumerator<Value> GetEnumerator() => _entries.Take(Count).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}