namespace DistIL.IR.Utils;

public class SourceLocationPrintDecorator : IPrintDecorator
{
    public static SourceLocationPrintDecorator Instance { get; } = new();

    public void DecorateInst(PrintContext ctx, Instruction inst)
    {
        if (inst.DebugLoc != null) {
            ctx.Print($" @ {inst.DebugLoc}", PrintToner.Comment);
        }
    }
}