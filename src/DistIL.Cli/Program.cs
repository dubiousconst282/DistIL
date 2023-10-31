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
    resolver.AddSearchPaths(new[] { Path.GetDirectoryName(Path.GetFullPath(options.InputPath))! });
    resolver.AddSearchPaths(options.ResolverPaths);

    if (!options.NoResolverFallback) {
        resolver.AddTrustedSearchPaths();
    }

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
        TrackAndLogStats = true
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

    // TODO: this segment is quite expansive, avoid repeating it too many times
    var simplifySeg = manager.AddPasses()
        .Apply<SimplifyInsts>()
        .Apply<SimplifyCFG>()
        .Apply<DeadCodeElim>()
        .RepeatUntilFixedPoint(maxIters: 3);

    manager.AddPasses()
        .Apply<ValueNumbering>()
        .Apply<LoopStrengthReduction>()
        .IfChanged(simplifySeg);

    manager.AddPasses()
        .Apply<LoopVectorizer>()
        .IfChanged(simplifySeg);

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

    var methods = PassManager.GetCandidateMethodsFromIL(comp.Module);
    options.FilterPassCandidates(methods);
    manager.Run(methods);
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

    [Option("no-resolver-fallback", HelpText = "Don't use fallback search paths for module resolution.")]
    public bool NoResolverFallback { get; set; } = false;

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

    [Option("bisect", HelpText = "Colon-separated list of probabilities used to randomly disable optimizations from methods.")]
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

        // Fuzzy bisect will randomly filter-out methods based on a list of probabilities.
        // Scripts can brute-force this list to easily find problematic methods.
        if (BisectFilter != null) {
            string[] parts = BisectFilter.Split(':');
            int sourceCount = candidates.Count;

            for (int i = 1; i < parts.Length; i++) {
                var rng = new Random(123 + i * 456);
                float prob = float.Parse(parts[i]) / 100.0f;

                candidates.RemoveAll(m => rng.NextSingle() < prob);
            }
            Console.WriteLine($"Fuzzy bisecting methods {candidates.Count}/{sourceCount}");
            File.WriteAllLines("bisect_log.txt", candidates.Select(RenderMethodSig));
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