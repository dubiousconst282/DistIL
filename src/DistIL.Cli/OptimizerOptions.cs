using CommandLine;

using DistIL;

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

    [Option("dump-filter", HelpText = "Filters methods to be dumped using a wildcard pattern.")]
    public string? DumpMethodFilter { get; set; }

    [Option("pass-filter", HelpText = "Filters methods to be transformed using a wildcard pattern.")]
    public string? PassMethodFilter { get; set; }

    [Option("bisect", HelpText = "Colon-separated list of probabilities used to randomly disable optimizations from methods.")]
    public string? BisectFilter { get; set; }

    [Option("verbosity", HelpText = "Specifies logging verbosity.\n")]
    public LogLevel Verbosity { get; set; } = LogLevel.Info;
}