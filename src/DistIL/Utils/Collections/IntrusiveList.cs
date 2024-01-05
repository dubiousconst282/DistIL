namespace DistIL.Util;

/// <summary> Interface providing efficient access and manipulation of intrusive doubly-linked lists. </summary>
public interface IIntrusiveList<TList, TNode>
    where TNode : class
{
    static abstract ref TNode? First(TList list);
    static abstract ref TNode? Last(TList list);

    static abstract ref TNode? Next(TNode node);
    static abstract ref TNode? Prev(TNode node);

    public static void InsertAfter<TAcc>(TList list, TNode? after, TNode node)
        where TAcc : struct, IIntrusiveList<TList, TNode>
    {
        InsertRangeAfter<TAcc>(list, after, node, node);
    }

    public static void InsertBefore<TAcc>(TList list, TNode? before, TNode node)
        where TAcc : struct, IIntrusiveList<TList, TNode>
    {
        InsertRangeBefore<TAcc>(list, before, node, node);
    }

    public static void RemoveRange<TAcc>(TList list, TNode rangeFirst, TNode rangeLast)
        where TAcc : struct, IIntrusiveList<TList, TNode>
    {
        ref var prev = ref TAcc.Prev(rangeFirst);
        ref var next = ref TAcc.Next(rangeLast);

        if (prev != null) {
            TAcc.Next(prev) = next;
        } else {
            TAcc.First(list) = next;
        }

        if (next != null) {
            TAcc.Prev(next) = prev;
        } else {
            TAcc.Last(list) = prev;
        }
    }

    /// <summary> Inserts a range of nodes after <paramref name="after"/> (null meaning before the first node). </summary>
    public static void InsertRangeAfter<TAcc>(TList list, TNode? after, TNode rangeFirst, TNode rangeLast)
        where TAcc : struct, IIntrusiveList<TList, TNode>
    {
        if (after != null) {
            Debug.Assert(after != rangeFirst);

            ref var next = ref TAcc.Next(after);

            TAcc.Prev(rangeFirst) = after;
            TAcc.Next(rangeLast) = next;

            if (next != null) {
                TAcc.Prev(next) = rangeLast;
            } else {
                Debug.Assert(after == TAcc.Last(list));
                TAcc.Last(list) = rangeLast;
            }
            next = rangeFirst;
        } else {
            ref var listFirst = ref TAcc.First(list);

            TAcc.Prev(rangeFirst) = null;
            TAcc.Next(rangeLast) = listFirst;

            if (listFirst != null) {
                TAcc.Prev(listFirst) = rangeLast;
            }
            listFirst = rangeFirst;
            TAcc.Last(list) ??= rangeLast;
        }
    }

    /// <summary> Inserts a range of nodes before <paramref name="before"/> (null meaning before the first node). </summary>
    public static void InsertRangeBefore<TAcc>(TList list, TNode? before, TNode rangeFirst, TNode rangeLast)
        where TAcc : struct, IIntrusiveList<TList, TNode>
    {
        if (before != null) {
            Debug.Assert(before != rangeFirst);

            ref var prev = ref TAcc.Prev(before);

            TAcc.Prev(rangeFirst) = prev;
            TAcc.Next(rangeLast) = before;

            if (prev != null) {
                TAcc.Next(prev) = rangeFirst;
            } else {
                Debug.Assert(before == TAcc.First(list));
                TAcc.First(list) = rangeFirst;
            }
            prev = rangeLast;
        } else {
            InsertRangeAfter<TAcc>(list, null, rangeFirst, rangeLast);
        }
    }
}