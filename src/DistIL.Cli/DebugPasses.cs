using System.Text.RegularExpressions;

using DistIL;
using DistIL.AsmIO;
using DistIL.IR.Utils;
using DistIL.Passes;

class DumpPass : IMethodPass
{
    public string BaseDir { get; init; } = null!;
    public Predicate<MethodDef>? Filter { get; init; }
    public DumpFormats Formats { get; init; }

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        if (Filter == null || Filter.Invoke(ctx.Method.Definition)) {
            var def = ctx.Method.Definition;
            string name = $"{def.DeclaringType.Name}::{def.Name}";

            // Escape all Windows forbidden characters to prevent issues with NTFS partitions on Linux
            name = Regex.Replace(name, @"[\x00-\x1F:*?\/\\""<>|]", "_");

            while (File.Exists($"{BaseDir}/{name}.txt")) {
                name += HashCode.Combine(name) % 10;
            }

            if (Formats.HasFlag(DumpFormats.Graphviz)) {
                IRPrinter.ExportDot(ctx.Method, $"{BaseDir}/{name}.dot");
            }
            if (Formats.HasFlag(DumpFormats.Plaintext)) {
                IRPrinter.ExportPlain(ctx.Method, $"{BaseDir}/{name}.txt");
            }
            if (Formats.HasFlag(DumpFormats.Forest)) {
                IRPrinter.ExportForest(ctx.Method, $"{BaseDir}/{name}_forest.txt");
            }
        }
        return MethodInvalidations.None;
    }
}
[Flags]
enum DumpFormats
{
    None = 0,
    Graphviz = 1 << 0,
    Plaintext = 1 << 1,
    Forest = 1 << 2
}

class VerificationPass : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var diags = IRVerifier.Diagnose(ctx.Method);

        if (diags.Count > 0) {
            using var scope = ctx.Logger.Push(new LoggerScopeInfo("DistIL.IR.Verification"), $"Bad IR in '{ctx.Method}'");

            foreach (var diag in diags) {
                ctx.Logger.Warn(diag.ToString());
            }
        }
        return MethodInvalidations.None;
    }
}