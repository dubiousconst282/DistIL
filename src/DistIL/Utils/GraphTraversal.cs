namespace DistIL.Util;

public class GraphTraversal
{
    public static void DepthFirst<TNode>(
        TNode entry,
        Func<TNode, List<TNode>> getChildren,
        Action<TNode>? preVisit = null,
        Action<TNode>? postVisit = null
    ) where TNode : class
    {
        var pending = new ArrayStack<(TNode Node, int Index)>();
        var visited = new RefSet<TNode>();

        visited.Add(entry);
        pending.Push((entry, 0));
        preVisit?.Invoke(entry);

        while (!pending.IsEmpty) {
            ref var top = ref pending.Top;
            var children = getChildren(top.Node);

            if (top.Index < children.Count) {
                var child = children[top.Index++];

                if (visited.Add(child)) {
                    pending.Push((child, 0));
                    preVisit?.Invoke(child);
                }
            } else {
                postVisit?.Invoke(top.Node);
                pending.Pop();
            }
        }
    }

    public static void DepthFirst(
        BasicBlock entry,
        Action<BasicBlock>? preVisit = null,
        Action<BasicBlock>? postVisit = null
    )
    {
        var pending = new ArrayStack<(BasicBlock Node, BasicBlock.SuccIterator Itr)>();
        var visited = new RefSet<BasicBlock>();

        visited.Add(entry);
        pending.Push((entry, entry.Succs));
        preVisit?.Invoke(entry);

        while (!pending.IsEmpty) {
            ref var top = ref pending.Top;

            if (top.Itr.MoveNext()) {
                var child = top.Itr.Current;

                if (visited.Add(child)) {
                    pending.Push((child, child.Succs));
                    preVisit?.Invoke(child);
                }
            } else {
                postVisit?.Invoke(top.Node);
                pending.Pop();
            }
        }
    }
}