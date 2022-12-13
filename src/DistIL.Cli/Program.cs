using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;

using CommandLine;
using CommandLine.Text;

using DistIL.AsmIO;
using DistIL.CodeGen.Cil;
using DistIL.Frontend;
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
    Console.WriteLine(HelpText.AutoBuild(result));
}

static void RunOptimizer(OptimizerOptions options)
{
    var resolver = new ModuleResolver();
    resolver.AddSearchPaths(new[] { Path.GetDirectoryName(options.InputPath)! });
    resolver.AddSearchPaths(options.ResolverPaths);
    resolver.AddTrustedSearchPaths();

    var module = resolver.Load(options.InputPath);

    var mp1 = new MethodPassManager();
    mp1.Add(new SimplifyCFG());
    mp1.Add(new SsaTransform());

    var mp2 = new MethodPassManager();
    mp2.Add(new ExpandLinq(module));
    mp2.Add(new SimplifyInsts(module)); //lambdas and devirtualization
    mp2.Add(new InlineMethods());
    mp2.Add(new ScalarReplacement());
    mp2.Add(new SimplifyInsts(module));
    //mp2.Add(new LoopInvariantCodeMotion());
    mp2.Add(new ValueNumbering());
    mp2.Add(new DeadCodeElim());
    mp2.Add(new SimplifyCFG());

    var mp3 = new MethodPassManager();
    mp3.Add(new RemovePhis());

    if (options.DumpDir != null) {
        if (options.PurgeDumps && Directory.Exists(options.DumpDir)) {
            Directory.Delete(options.DumpDir, true);
        }
        Directory.CreateDirectory(options.DumpDir);

        mp2.Add(new DumpPass() {
            BaseDir = options.DumpDir,
            Formats = options.DumpFmts,
            Filter = options.GetCompiledFilter()
        });
    }

    var pm = new ModulePassManager();
    pm.Add(new ImportPass() {
        Filter = options.GetCompiledFilter(true),
        BisectFilter = options.BisectFilter
    });
    pm.Add(mp1);
    pm.Add(mp2);
    pm.Add(mp3);
    pm.Add(new ExportPass());

    pm.Run(module);

    AddIgnoreAccessAttrib(module, new[] { module.AsmName.Name!, "System.Private.CoreLib" });

    string? outputPath = options.OutputPath;

    if (outputPath == null) {
        File.Move(options.InputPath, Path.ChangeExtension(options.InputPath, ".bak"), overwrite: false);
        outputPath = options.InputPath;
    }
    module.Save(outputPath);
}
static void AddIgnoreAccessAttrib(ModuleDef module, IEnumerable<string> assemblyNames)
{
    var attribType = module.CreateType(
        "System.Runtime.CompilerServices", "IgnoresAccessChecksToAttribute",
        TypeAttributes.BeforeFieldInit | TypeAttributes.Class,
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

[Verb("opt", isDefault: true, HelpText = "Optimizes a module")]
class OptimizerOptions
{
    [Option('i', Required = true, HelpText = "Input module file path")]
    public string InputPath { get; set; } = null!;

    [Option('o', HelpText = "Output module file path")]
    public string? OutputPath { get; set; } = null;

    [Option('r', HelpText = "Module resolver search paths")]
    public IEnumerable<string> ResolverPaths { get; set; } = null!;

    [Option("dump-dir", HelpText = "Output directory for IR dumps")]
    public string? DumpDir { get; set; } = null;

    [Option("dump-fmts", HelpText = "IR dump formats")]
    public DumpFormats DumpFmts { get; set; } = DumpFormats.Graphviz;

    [Option("purge-dumps", HelpText = "Delete all files in `dump-dir`.")]
    public bool PurgeDumps { get; set; }

    [Option("filter", HelpText = kFilterHelp)]
    public string? MethodFilter { get; set; }

    [Option("bisect", HelpText = "Binary searches for methods with bad codegen using a string composed by `g`ood and `b`ad characters.")]
    public string? BisectFilter { get; set; }

    const string kFilterHelp = """
        Filters methods to optimize or dump using a wildcard pattern: 
          [TypeName::] MethodName [(ParType1, ParType2, ...)]
        Multiple patterns can be separated with '|'. 
        The optimizer is only affected if this is prefixed with '!'.
        """;

    Predicate<MethodDef>? _cachedFilter;

    public Predicate<MethodDef>? GetCompiledFilter(bool isForImport = false)
    {
        if (MethodFilter == null) {
            return null;
        }
        bool appliesToImport = MethodFilter.StartsWith("!");
        var str = MethodFilter.Substring(appliesToImport ? 1 : 0);
        _cachedFilter ??= CompileFilter(str);

        return !isForImport || appliesToImport ? _cachedFilter : null;
    }

    private static Predicate<MethodDef> CompileFilter(string pattern)
    {
        //Pattern :=  (ClassName  "::")? MethodName ( "(" Seq{TypeName} ")" )?  ("|" Pattern)?
        pattern = string.Join("|", pattern.Split('|').Select(part => {
            var tokens = Regex.Match(part, @"^(?:(.+)::)?(.+?)(?:\((.+)\))?$");

            var typeToken = WildcardToRegex(tokens.Groups[1].Value);
            var methodToken = WildcardToRegex(tokens.Groups[2].Value);
            var sigToken = WildcardToRegex(tokens.Groups[3].Value, true);

            return @$"(?:{typeToken}::{methodToken}\({sigToken}\))";
        }));
        var regex = new Regex("^" + pattern + "$", RegexOptions.CultureInvariant);

        return (m) => {
            var pars = m.ParamSig.Skip(m.IsInstance ? 1 : 0);
            var name = $"{m.DeclaringType.Name}::{m.Name}({string.Join(',', pars.Select(p => p.Type.Name))})";
            return regex.IsMatch(name);
        };

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
}

class DumpPass : MethodPass
{
    public string BaseDir { get; init; } = null!;
    public Predicate<MethodDef>? Filter { get; init; }
    public DumpFormats Formats { get; init; }

    public override void Run(MethodTransformContext ctx)
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

        var diags = IRVerifier.Diagnose(ctx.Method);
        if (diags.Count > 0) {
            Console.WriteLine($"BadIR in {ctx.Method}:\n  {string.Join("\n  ", diags)}");
        }
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

class ImportPass : ModulePass
{
    public Predicate<MethodDef>? Filter { get; init; }
    public string? BisectFilter { get; init; }

    public override void Run(ModuleTransformContext ctx)
    {
        int index = 0;
        var bisectRange = default(AbsRange);

        if (BisectFilter != null) {
            bisectRange = GetBisectRange(ctx.DefinedMethods.Count, BisectFilter);
            File.Delete("bisect_log.txt");
            Console.WriteLine($"Bisecting methods [{bisectRange}], ~{(int)Math.Log2(bisectRange.Length):0} steps left.");
        }

        foreach (var method in ctx.DefinedMethods) {
            index++;

            if (Filter != null && !Filter.Invoke(method)) continue;

            if (!bisectRange.IsEmpty) {
                if (!bisectRange.Contains(index)) continue;

                File.AppendAllText("bisect_log.txt", $"{index} {method}\n");
            }

            try {
                method.Body = ILImporter.ImportCode(method);
            } catch (Exception ex) {
                Console.WriteLine($"FailImp: {method} {ex.Message}");
            }
        }
    }

    private static AbsRange GetBisectRange(int count, string filter)
    {
        int start = 0, end = count;

        for (int i = 0; i <= filter.Length; i++) {
            int mid = (start + end) >>> 1;

            if (i == filter.Length || char.ToUpper(filter[i]) == 'B') {
                end = mid;
            } else {
                start = mid;
            }
        }
        return (start, end);
    }
}
class ExportPass : ModulePass
{
    public override void Run(ModuleTransformContext ctx)
    {
        foreach (var method in ctx.DefinedMethods) {
            if (method.Body == null) continue;

            try {
                method.ILBody = ILGenerator.Generate(method.Body);
            } catch (Exception ex) {
                Console.WriteLine($"FailEmit: {method} {ex.Message}");
            }
        }
    }
}