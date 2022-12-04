namespace DistIL.IR.Utils.Parser;

internal record Node
{
    public AbsRange Location = default;

    //Stuff to exclude Location field
    public override int GetHashCode() => 0;
    public virtual bool Equals(Node? other) => other is not null;
    protected virtual bool PrintMembers(StringBuilder sb) => false;
}
internal record IdNode(string Name) : Node;

//Bound Variable/Argument identifier
internal record BoundNode(Value Value) : Node;
internal record VarNode(string Name) : Node
{
    public TypeDesc? Type;
}

//Instructions
internal record InstNode(
    string Opcode, List<Node> Operands, 
    TypeDesc? ResultType = null, string? ResultVar = null
) : Node
{
    public virtual bool Equals(InstNode? other)
        => other != null && other.Opcode == Opcode && 
           other.Operands.SequenceEqual(Operands) &&
           other.ResultType == ResultType && other.ResultVar == ResultVar;

    public override int GetHashCode()
        => HashCode.Combine(Opcode, ResultVar);
}

//Other
internal record BlockNode(string Label, List<InstNode> Code) : Node;

internal record ProgramNode(List<BlockNode> Blocks) : Node;