using System.Reflection;
using System.Text.RegularExpressions;

using CommandLine;
using CommandLine.Text;

using DistIL;
using DistIL.AsmIO;
using DistIL.Passes;

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
    resolver.AddSearchPaths([Path.GetDirectoryName(Path.GetFullPath(options.InputPath))!]);
    resolver.AddSearchPaths(options.ResolverPaths);

    if (!options.NoResolverFallback) {
        resolver.AddTrustedSearchPaths();
    }

    var module = resolver.Load(options.InputPath);

    var comp = new Compilation(module, logger, new CompilationSettings());
    RunPasses(options, comp);

    AddIgnoreAccessAttrib(module, [module.AsmName.Name!, "System.Private.CoreLib"]);

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
        .Apply<SimplifyInsts>(); // lambdas and devirtualization

    manager.AddPasses(applyIndependently: true) // this is so that e.g. all callees are in SSA before inlining.
        .Apply<InlineMethods>();

    manager.AddPasses()
        .Apply<ScalarReplacement>()
        .IfChanged(c => c.Apply<SsaPromotion>()
                         .Apply<InlineMethods>()) // SROA+SSA uncovers new devirtualization oportunities
        .RepeatUntilFixedPoint(maxIters: 3);

    var simplifySeg = manager.AddPasses()
        .Apply<SimplifyInsts>()
        .Apply<SimplifyCFG>()
        .Apply<DeadCodeElim>()
        .RepeatUntilFixedPoint(maxIters: 2);

    manager.AddPasses()
        .Apply<ValueNumbering>()
        .Apply<PresizeLists>()
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
            Filter = CompileFilter(options.DumpMethodFilter)
        });
    }

    var methods = PassManager.GetCandidateMethodsFromIL(comp.Module, GetCandidateFilter(options, comp));

    if (options.BisectFilter != null) {
        ApplyFuzzyBisect(options.BisectFilter, methods);
    }
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
        [new ParamDef(attribType, "this"), new ParamDef(PrimType.String, "assemblyName")],
        MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig
    );
    attribCtor.ILBody = new ILMethodBody() {
        Instructions = new[] { new ILInstruction(ILCode.Ret) }
    };

    var assemblyAttribs = module.GetCustomAttribs(forAssembly: true);
    foreach (var name in assemblyNames) {
        assemblyAttribs.Add(new CustomAttrib(attribCtor, [name]));
    }
}

static PassManager.CandidateMethodFilter GetCandidateFilter(OptimizerOptions options, Compilation comp)
{
    var filter = CompileFilter(options.PassMethodFilter);

    return (caller, method) => {
        if (method.Module != comp.Module) {
            return false;
        }
        if (options.FilterUnmarked && !IsMarkedForOpts(method)) {
            // Include inner lambdas and local functions within marked parent methods
            // to enable inlining into marked methods. See #27
            if (!(IsGeneratedInnerMethod(method) && caller != null && IsMarkedForOpts(caller))) {
                return false;
            }
        }
        // TODO: support for something like ILLink root descs file, see #30
        if (filter != null && !filter.Invoke(method)) {
            return false;
        }
        return true;
    };

    static bool IsMarkedForOpts(MethodDef method)
    {
        return (FindOptFlag(method) ?? FindOptFlag(method.DeclaringType)) is true;
    }
    static bool? FindOptFlag(ModuleEntity entity)
    {
        foreach (var attr in entity.GetCustomAttribs()) {
            if (attr.Type.Namespace != "DistIL.Attributes") continue;
            if (attr.Type.Name == "OptimizeAttribute") return true;
            if (attr.Type.Name == "DoNotOptimizeAttribute") return false;
        }
        return null;
    }
    // Checks if the given method has a mangled lambda or local function name.
    // - https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNames.cs
    static bool IsGeneratedInnerMethod(MethodDef method)
    {
        return method.Name.StartsWith('<') && (method.Name.Contains(">b__") || method.Name.Contains(">g__"));
    }
}

// Fuzzy bisect will randomly filter-out methods based on a list of probabilities.
// Scripts can brute-force this list to easily find problematic methods.
static void ApplyFuzzyBisect(string filterStr, List<MethodDef> candidates)
{
    string[] parts = filterStr.Split(':');
    int sourceCount = candidates.Count;

    for (int i = 1; i < parts.Length; i++) {
        var rng = new Random(123 + i * 456);
        float prob = float.Parse(parts[i]) / 100.0f;

        candidates.RemoveAll(m => rng.NextSingle() < prob);
    }
    Console.WriteLine($"Fuzzy bisecting methods {candidates.Count}/{sourceCount}");
    File.WriteAllLines("bisect_log.txt", candidates.Select(RenderMethodSig));
}

static Predicate<MethodDef>? CompileFilter(string? pattern)
{
    if (pattern == null) {
        return null;
    }

    // Pattern :=  (ClassName  "::")? MethodName ( "(" Seq{TypeName} ")" )?  ("|" Pattern)?
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
        // Based on https://stackoverflow.com/a/30300521
        if (normalizeSeq) {
            value = Regex.Replace(value, @"\s*,\s*", ",");
        }
        value = Regex.Escape(value);
        return value.Replace("\\?", ".").Replace("\\*", ".*");
    }
}
static string RenderMethodSig(MethodDef def)
{
    var pars = def.ParamSig.Skip(def.IsInstance ? 1 : 0);
    return $"{def.DeclaringType.Name}::{def.Name}({string.Join(',', pars.Select(p => p.Type.Name))})";
}