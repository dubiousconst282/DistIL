namespace DistIL.IR.Utils;

public class SourceLocationPrintDecorator : IPrintDecorator
{
    public static SourceLocationPrintDecorator Instance { get; } = new();

    public void DecorateInst(PrintContext ctx, Instruction inst)
    {
        if (inst.Location.IsNull) return;

        var definingMethod = inst.Block.Method.Definition;
        var originalMethod = inst.Location.GetMethod(definingMethod.Module.Resolver);

        ctx.Print($" @ IL_{inst.Location.Offset:X4}", PrintToner.Comment);

        if (originalMethod != null && originalMethod != definingMethod) {
            ctx.Print($" in {originalMethod.DeclaringType.Name}::{originalMethod.Name}", PrintToner.Comment);
        }
    }
}