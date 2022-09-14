namespace DistIL.IR.Utils;

using DistIL.IR.Utils.Parser;

public class IRParser
{
    public static void Populate(MethodBody method, string sourceCode)
    {
        var ctx = new ParserContext(sourceCode, method.Definition.Module.Resolver);
        Populate(method, ctx);
    }

    public static void Populate(MethodBody method, ParserContext ctx)
    {
        var program = new AstParser(ctx).ParseProgram();
        if (ctx.Errors.Count > 0) {
            throw new FormatException("Failed to parse source code");
        }
        new Binder(ctx).Process(program);
        new Materializer(ctx, method).Process(program);
    }
}