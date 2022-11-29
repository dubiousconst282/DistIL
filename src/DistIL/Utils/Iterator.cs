namespace DistIL.Util;

using System.Collections;

/// <summary> A lightweight alternative for <see cref="IEnumerator{T}"/>, without all the legacy cruft. </summary>
public interface Iterator<out T> : Iterator
{
    T Current { get; }
    bool MoveNext();
}
public interface Iterator { }

public static class Iterators
{
    public static Itr GetEnumerator<Itr>(this Itr itr) where Itr : Iterator => itr;
    public static EnumerableAdapter<T> AsEnumerable<T>(this Iterator<T> itr) => new(itr);

    //We don't use a generic param for `Iterator<T>` because the language can't infer generic arguments for uses.
    //Maybe we should apply AggressiveInlining in all funcs so the JIT can devirt and avoid boxing,
    //but I don't feel like this matters much just yet.

    public static bool Any<T>(this Iterator<T> itr, Func<T, bool> predicate)
    {
        while (itr.MoveNext()) {
            if (predicate(itr.Current)) {
                return true;
            }
        }
        return false;
    }
    
    public static bool All<T>(this Iterator<T> itr, Func<T, bool> predicate)
    {
        while (itr.MoveNext()) {
            if (!predicate(itr.Current)) {
                return false;
            }
        }
        return true;
    }

    /// <summary> Returns the number of *remaining* elements in the iterator. </summary>
    public static int Count<T>(this Iterator<T> itr)
    {
        int count = 0;
        while (itr.MoveNext()) {
            count++;
        }
        return count;
    }
    /// <summary> Returns the number of all *remaining* elements in the iterator that passes `predicate`. </summary>
    public static int Count<T>(this Iterator<T> itr, Func<T, bool> predicate)
    {
        int count = 0;
        while (itr.MoveNext()) {
            if (predicate(itr.Current)) {
                count++;
            }
        }
        return count;
    }

    /// <summary> Returns the *next* element in the iterator. </summary>
    public static T First<T>(this Iterator<T> itr)
    {
        Ensure.That(itr.MoveNext(), "Iterator did not yield another element.");
        return itr.Current;
    }

    /// <summary> Returns the *next* element in the iterator, or `default` if there are no more elements. </summary>
    public static T? FirstOrDefault<T>(this Iterator<T> itr)
    {
        return itr.MoveNext() ? itr.Current : default;
    }

    /// <summary> Returns the *next* element in the iterator that matches `predicate`, or `default` if none was found. </summary>
    public static T? FirstOrDefault<T>(this Iterator<T> itr, Func<T, bool> predicate)
    {
        while (itr.MoveNext()) {
            var value = itr.Current;
            if (predicate(value)) {
                return value;
            }
        }
        return default;
    }

    public static OfTypeIterator<T> OfType<T>(this Iterator<object> itr) => new(itr);

    public static List<T> ToList<T>(this Iterator<T> itr)
    {
        var list = new List<T>();
        while (itr.MoveNext()) {
            list.Add(itr.Current);
        }
        return list;
    }

    public class EnumerableAdapter<T> : IEnumerable<T>, IEnumerator<T>
    {
        readonly Iterator<T> _src;

        public EnumerableAdapter(Iterator<T> src) => _src = src;

        public T Current => _src.Current;
        public bool MoveNext() => _src.MoveNext();
        public IEnumerator<T> GetEnumerator() => this;

        public void Reset() => throw new InvalidOperationException();
        public void Dispose() => GC.SuppressFinalize(this);

        IEnumerator IEnumerable.GetEnumerator() => this;
        object? IEnumerator.Current => Current;
    }
    public struct OfTypeIterator<T> : Iterator<T>
    {
        readonly Iterator<object> _src;

        public OfTypeIterator(Iterator<object> src) => _src = src;

        public T Current => (T)_src.Current;

        public bool MoveNext()
        {
            while (_src.MoveNext()) {
                if (_src.Current is T) {
                    return true;
                }
            }
            return false;
        }
    }
}