namespace DistIL.Frontend;

using ExceptionRegionKind = System.Reflection.Metadata.ExceptionRegionKind;

//Based on https://github.com/icsharpcode/ILSpy/blob/master/ICSharpCode.Decompiler/Disassembler/ILStructure.cs
internal class RegionNode
{
    public RegionKind Kind;
    public int StartOffset, EndOffset;
    public RegionNode? Parent;
    public List<RegionNode> Children = new();

    public RegionNode FindEnclosing(int start, int end)
    {
        foreach (var child in Children) {
            //use <= for end-offset comparisons because both end and EndOffset are exclusive
            if (start >= child.StartOffset && end <= child.EndOffset) {
                return child.FindEnclosing(start, end);
            } else if (!(child.EndOffset <= start || end <= child.StartOffset)) {
                //child overlaps with arguments
                if (!(start <= child.StartOffset && child.EndOffset <= end)) {
                    //Invalid nesting, can't build a tree.
                    throw new InvalidOperationException("Invalid region nesting");
                }
            }
        }
        Debug.Assert(start >= StartOffset && end <= EndOffset); //should be the root node
        return this;
    }

    public RegionNode FindEnclosing(int offset) => FindEnclosing(offset, offset + 1);

    public bool Contains(int offset) => offset >= StartOffset && offset <= EndOffset;

    public static RegionNode? BuildTree(ExceptionRegion[] clauses)
    {
        if (clauses.Length == 0) {
            return null;
        }
        var root = new RegionNode() {
            Kind = RegionKind.Root,
            StartOffset = 0,
            EndOffset = int.MaxValue
        };
        foreach (var clause in clauses) {
            var handlerKind = clause.Kind == ExceptionRegionKind.Finally ? RegionKind.Finally : RegionKind.Catch;
            root.Add(RegionKind.Protected, clause.TryStart, clause.TryEnd);
            root.Add(handlerKind, clause.HandlerStart, clause.HandlerEnd);

            if (clause.Kind == ExceptionRegionKind.Filter) {
                root.Add(RegionKind.Filter, clause.FilterStart, clause.FilterEnd);
            }
        }
        return root;
    }

    private void Add(RegionKind kind, int start, int end)
    {
        var enclosingNode = FindEnclosing(start, end);
        if (enclosingNode.StartOffset == start && enclosingNode.EndOffset == end) return;
        
        var newNode = new RegionNode() { Kind = kind, StartOffset = start, EndOffset = end };
        var enclosedChildren = enclosingNode.Children;

        //Move existing children to `newNode` if it encloses them
        for (int i = 0; i < enclosedChildren.Count; i++) {
            var child = enclosedChildren[i];
            if (child.StartOffset >= start && child.EndOffset <= end) {
                newNode.AddChild(child);
                enclosedChildren.RemoveAt(i--);
            }
        }
        enclosingNode.AddChild(newNode);
    }
    private void AddChild(RegionNode node)
    {
        node.Parent = this;
        Children.Add(node);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        Print(this, "");
        return sb.ToString();

        void Print(RegionNode node, string indent)
        {
            sb.Append($"{indent}{node.Kind} in {node.StartOffset}..{node.EndOffset}\n");
            foreach (var child in node.Children) {
                Print(child, indent + "  ");
            }
        }
    }
}
internal enum RegionKind
{
    Root, Protected, Catch, Finally, Filter
}