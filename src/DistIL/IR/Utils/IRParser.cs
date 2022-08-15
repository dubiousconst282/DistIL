namespace DistIL.IR.Utils;

using DistIL.IR.Utils.Parser;

public class IRParser
{
    public static void Populate(MethodBody method, string sourceCode)
    {
        var ctx = new ParserContext(sourceCode, method.Definition.Module.Resolver);
        Populate(method, ctx);
    }

    internal static void Populate(MethodBody method, ParserContext ctx)
    {
        var program = new AstParser(ctx).ParseProgram();
        new Binder(ctx).Process(program);
        new Materializer(ctx, method).Process(program);
    }
}