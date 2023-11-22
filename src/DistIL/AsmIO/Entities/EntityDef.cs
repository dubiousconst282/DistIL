namespace DistIL.AsmIO;

public abstract class EntityDesc : IPrintable
{
    public abstract void Print(PrintContext ctx);
    public virtual void PrintAsOperand(PrintContext ctx) => Print(ctx);
    public override string ToString() => PrintContext.ToString(this);
}
/// <summary> Represents an entity defined in a module. </summary>
public interface ModuleEntity
{
    ModuleDef Module { get; }

    /// <summary> Returns the list of custom attributes applied to this entity. </summary>
    /// <param name="readOnly">When true, the returned list may be read-only. If you intend to add or remove attributes, set this to false. </param>
    IList<CustomAttrib> GetCustomAttribs(bool readOnly = true);
}

public abstract class MemberDesc : EntityDesc
{
    public abstract TypeDesc DeclaringType { get; }
    public abstract string Name { get; set; }
}