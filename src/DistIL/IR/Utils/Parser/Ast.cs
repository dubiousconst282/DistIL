namespace DistIL.IR.Utils.Parser;

internal record Node
{
    public (int Start, int End) Location = (-1, -1);

    //Stuff to ignore Location field
    public override int GetHashCode() => base.GetHashCode();
    public virtual bool Equals(Node? other) => other is not null;
    protected virtual bool PrintMembers(StringBuilder sb) => false;
}
internal record IdNode(string Name) : Node;
internal record ConstNode(Const Value) : Node;

//Bound Variable/Argument identifier
internal record VarNode(string Name) : Node
{
    public TypeNode? Type;
}

//Types
internal record TypeNode : Node;
internal record BasicTypeNode(string Name) : TypeNode;
internal record NestedTypeNode(TypeNode Parent, string ChildName) : TypeNode;

internal record ArrayTypeNode(TypeNode ElemType) : TypeNode;
internal record PointerTypeNode(TypeNode ElemType) : TypeNode;
internal record ByrefTypeNode(TypeNode ElemType) : TypeNode;

internal record TypeSpecNode(TypeNode Definition, TypeNode[] ArgTypes) : TypeNode
{
    public virtual bool Equals(TypeSpecNode? other)
        => other != null && other.Definition == Definition && other.ArgTypes.SequenceEqual(ArgTypes);

    public override int GetHashCode()
        => Definition.GetHashCode();
}
internal record GenParamTypeNode(int Index, bool IsMethodParam = false) : TypeNode;

//Instructions
internal record InstNode(
    string Opcode, List<Node> Operands, 
    TypeNode? ResultType = null, string? ResultVar = null
) : Node
{
    public virtual bool Equals(InstNode? other)
        => other != null && other.Opcode == Opcode && 
           other.Operands.SequenceEqual(Operands) &&
           other.ResultType == ResultType && other.ResultVar == ResultVar;

    public override int GetHashCode()
        => HashCode.Combine(Opcode, ResultVar);
}
internal record MethodNode(
    TypeNode Owner, string Name,
    List<TypeNode>? GenParams, 
    TypeNode RetType, List<TypeNode> Params
) : Node
{
    public virtual bool Equals(MethodNode? other)
    => other != null && other.Owner == Owner &&
       other.RetType == RetType &&
       other.Params.SequenceEqual(Params) &&
       (other.GenParams == null ? GenParams == null : GenParams != null && other.GenParams.SequenceEqual(GenParams));

    public override int GetHashCode()
        => HashCode.Combine(Owner, Name);
}
internal record FieldNode(TypeNode Owner, string Name) : Node;

//Other
internal record BlockNode(string Label, List<InstNode> Code) : Node;

internal record ProgramNode(
    List<(string Mod, string Ns)> Imports,
    List<BlockNode> Blocks
) : Node;