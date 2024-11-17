namespace DistIL.IR.Utils;

public class SourceLocationPrintDecorator : IPrintDecorator
{
    public static SourceLocationPrintDecorator Instance { get; } = new();

    public void DecorateInst(PrintContext ctx, Instruction inst)
    {
        if (inst.DebugLocation != null) {
            ctx.Print($" @ {inst.DebugLocation}", PrintToner.Comment);
        }
    }
}