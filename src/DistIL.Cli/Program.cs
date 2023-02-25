using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;

using CommandLine;
using CommandLine.Text;

using DistIL;
using DistIL.AsmIO;
using DistIL.IR.Utils;
using DistIL.Passes;
using DistIL.Util;

var parser = new CommandLine.Parser(c => {
    c.CaseInsensitiveEnumValues = true;
});
var result = parser
    .ParseArguments<OptimizerOptions>(args)
        .WithParsed(RunOptimizer);

if (result.Tag == ParserResultType.NotParsed) {
    var help = HelpText.AutoBuild(result, h => {
        h.AddEnumValuesToHelpText = true;
        return h;
    });
    Console.WriteLine(help);
}

static void RunOptimizer(OptimizerOptions options)
{
    var logger = new ConsoleLogger() { MinLevel = options.Verbosity };

    var resolver = new ModuleResolver(logger);
    resolver.AddSearchPaths(options.ResolverPaths);
    resolver.AddSearchPaths(new[] { Path.GetDirectoryName(options.InputPath)!, Environment.CurrentDirectory });
    resolver.AddTrustedSearchPaths();

    var module = resolver.Load(options.InputPath);

    var comp = new Compilation(module, logger, new CompilationSettings());
    RunPasses(options, comp);

    AddIgnoreAccessAttrib(module, new[] { module.AsmName.Name!, "System.Private.CoreLib" });

    string? outputPath = options.OutputPath;

    if (outputPath == null) {
        File.Move(options.InputPath, Path.ChangeExtension(options.InputPath, ".dll.bak"), overwrite: true);
        outputPath = options.InputPath;
    }
    module.Save(outputPath);
}
static void RunPasses(OptimizerOptions options, Compilation comp)
{
    var manager = new PassManager() {
        Compilation = comp,
        TrackAndLogStats = true,
        PassCandidateFilter = options.FilterPassCandidates
    };

    manager.AddPasses()
        .Apply<SimplifyCFG>()
        .Apply<SsaPromotion>()
        .Apply<ExpandLinq>()
        .Apply<SimplifyInsts>(); //lambdas and devirtualization

    manager.AddPasses(applyIndependently: true) //this is so that e.g. all callees are in SSA before inlining.
        .Apply<InlineMethods>()
        .IfChanged(c => c.Apply<SimplifyInsts>());

    manager.AddPasses()
        .Apply<ScalarReplacement>()
        .IfChanged(c => c.Apply<SsaPromotion>());

    manager.AddPasses()
        .Apply<SimplifyInsts>()
        .Apply<SimplifyCFG>()
        .Apply<DeadCodeElim>()
        .RepeatUntilFixedPoint(maxIters: 3);
    
    manager.AddPasses()
        .Apply<ValueNumbering>()
        .Apply<LoopStrengthReduction>()
        .IfChanged(c => c.Apply<DeadCodeElim>());

    if (comp.Logger.IsEnabled(LogLevel.Debug)) {
        manager.AddPasses().Apply<VerificationPass>();
    }

    if (options.DumpDir != null) {
        if (options.PurgeDumps && Directory.Exists(options.DumpDir)) {
            Directory.Delete(options.DumpDir, recursive: true);
        }
        Directory.CreateDirectory(options.DumpDir);

        manager.AddPasses().Apply(new DumpPass() {
            BaseDir = options.DumpDir,
            Formats = options.DumpFmts,
            Filter = options.GetMethodFilter()
        });
    }

    manager.Run();
}
static void AddIgnoreAccessAttrib(ModuleDef module, IEnumerable<string> assemblyNames)
{
    var attribType = module.CreateType(
        "System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute",
        TypeAttributes.BeforeFieldInit,
        module.Resolver.CoreLib.FindType("System", "Attribute")
    );
    var attribCtor = attribType.CreateMethod(
        ".ctor", PrimType.Void,
        ImmutableArray.Create(new ParamDef(attribType, "this"), new ParamDef(PrimType.String, "assemblyName")),
        MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig
    );
    attribCtor.ILBody = new ILMethodBody() {
        Instructions = new[] { new ILInstruction(ILCode.Ret) }
    };

    var assemblyAttribs = module.GetCustomAttribs(forAssembly: true);
    foreach (var name in assemblyNames) {
        assemblyAttribs.Add(new CustomAttrib(attribCtor, ImmutableArray.Create<object?>(name)));
    }
}

[Verb("opt", isDefault: true, HelpText = "Optimizes a module.")]
class OptimizerOptions
{
    [Option('i', Required = true, HelpText = "Input module file path.")]
    public string InputPath { get; set; } = null!;

    [Option('o', HelpText = "Output module file path. If unspecified, the input module will be overwritten.")]
    public string? OutputPath { get; set; } = null;

    [Option('r', HelpText = "Module resolver search paths.")]
    public IEnumerable<string> ResolverPaths { get; set; } = null!;

    [Option("filter-unmarked", HelpText = "Only transform methods and classes marked with `OptimizeAttribute`.")]
    public bool FilterUnmarked { get; set; } = false;

    [Option("dump-dir", HelpText = "Output directory for IR dumps.")]
    public string? DumpDir { get; set; } = null;

    [Option("dump-fmts", HelpText = "Comma-separated list of IR dump formats.\n")]
    public DumpFormats DumpFmts { get; set; } = DumpFormats.Graphviz;

    [Option("purge-dumps", HelpText = "Delete all files in `dump-dir`.")]
    public bool PurgeDumps { get; set; }

    [Option("filter", HelpText = kFilterHelp)]
    public string? MethodFilter { get; set; }

    [Option("bisect", HelpText = "Limits passes to methods within a log2 range based on a string composed by `g`ood and `b`ad characters. Used to find methods with bad codegen, similarly to `git bisect`.")]
    public string? BisectFilter { get; set; }

    [Option("verbosity", HelpText = "Specifies logging verbosity.\n")]
    public LogLevel Verbosity { get; set; } = LogLevel.Info;

    const string kFilterHelp = """
        Filters methods to optimize or dump using a wildcard pattern: 
          [TypeName::] MethodName [(ParType1, ParType2, ...)]
        Multiple patterns can be separated with '|'. 
        The optimizer is only affected if this is prefixed with '!'.
        """;

    Predicate<MethodDef>? _cachedFilter;

    public void FilterPassCandidates(List<MethodDef> candidates)
    {
        if (FilterUnmarked) {
            candidates.RemoveAll(m => (GetOptimizeAttr(m) ?? GetOptimizeAttr(m.DeclaringType)) is not true);

            static bool? GetOptimizeAttr(ModuleEntity entity)
            {
                foreach (var attr in entity.GetCustomAttribs()) {
                    if (attr.Type.Namespace != "DistIL.Attributes") continue;
                    if (attr.Type.Name == "OptimizeAttribute") return true;
                    if (attr.Type.Name == "DoNotOptimizeAttribute") return false;
                }
                return null;
            }
        }
        if (MethodFilter != null && MethodFilter.StartsWith('!')) {
            var pred = GetMethodFilter()!;
            candidates.RemoveAll(m => !pred.Invoke(m));
        }
        if (BisectFilter != null) {
            var bisectRange = GetBisectRange(candidates.Count, BisectFilter);
            Console.WriteLine($"Bisecting method range [{bisectRange}], ~{(int)Math.Log2(bisectRange.Length)} steps left.");

            candidates.RemoveRange(bisectRange.End, candidates.Count - bisectRange.End);
            candidates.RemoveRange(0, bisectRange.Start);

            File.WriteAllLines("bisect_log.txt", candidates.Select((m, i) => $"{bisectRange.Start + i}: {RenderMethodSig(m)}"));
        }
    }

    public Predicate<MethodDef>? GetMethodFilter()
    {
        if (MethodFilter == null) {
            return null;
        }
        return _cachedFilter ??= CompileFilter(MethodFilter);
    }

    private static Predicate<MethodDef> CompileFilter(string pattern)
    {
        pattern = pattern.TrimStart('!');
        
        //Pattern :=  (ClassName  "::")? MethodName ( "(" Seq{TypeName} ")" )?  ("|" Pattern)?
        pattern = string.Join("|", pattern.Split('|').Select(part => {
            var tokens = Regex.Match(part, @"^(?:(.+)::)?(.+?)(?:\((.+)\))?$");

            var typeToken = WildcardToRegex(tokens.Groups[1].Value);
            var methodToken = WildcardToRegex(tokens.Groups[2].Value);
            var sigToken = WildcardToRegex(tokens.Groups[3].Value, true);

            return @$"(?:{typeToken}::{methodToken}\({sigToken}\))";
        }));
        var regex = new Regex("^" + pattern + "$", RegexOptions.CultureInvariant);

        return (m) => regex.IsMatch(RenderMethodSig(m));

        static string WildcardToRegex(string value, bool normalizeSeq = false)
        {
            if (string.IsNullOrWhiteSpace(value)) {
                return ".*";
            }
            //Based on https://stackoverflow.com/a/30300521
            if (normalizeSeq) {
                value = Regex.Replace(value, @"\s*,\s*", ",");
            }
            value = Regex.Escape(value);
            return value.Replace("\\?", ".").Replace("\\*", ".*");
        }
    }
    private static string RenderMethodSig(MethodDef def)
    {
        var pars = def.ParamSig.Skip(def.IsInstance ? 1 : 0);
        return $"{def.DeclaringType.Name}::{def.Name}({string.Join(',', pars.Select(p => p.Type.Name))})";
    }
    private static AbsRange GetBisectRange(int count, string filter)
    {
        int start = 0, end = count;

        for (int i = 0; i < filter.Length; i++) {
            int mid = (start + end) >>> 1;

            if (char.ToUpper(filter[i]) == 'B') {
                end = mid;
            } else {
                start = mid;
            }
        }
        return (start, end);
    }
}

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

            //Escape all Windows forbidden characters to prevent issues with NTFS partitions on Linux
            name = Regex.Replace(name, @"[\x00-\x1F:*?\/\\""<>|]", "_");

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
    Graphviz    = 1 << 0,
    Plaintext   = 1 << 1,
    Forest      = 1 << 2
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