using System.Diagnostics;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.Parameters;
using BenchmarkDotNet.Toolchains.Results;

var toolchain = CsProjCoreToolchain.NetCoreApp80;
var defaultJob = Job.ShortRun
    .WithIterationCount(20)
    .WithToolchain(toolchain);

var config = DefaultConfig.Instance
    .AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByMethod, BenchmarkLogicalGroupRule.ByParams)
    .AddJob(defaultJob.AsBaseline())
    .AddJob(defaultJob.WithToolchain(new OptToolchain(toolchain)));

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);

class OptToolchain : Toolchain
{
    public OptToolchain(IToolchain baseToolchain)
        : base(
            baseToolchain + " + DistIL",
            baseToolchain.Generator,
            baseToolchain.Builder,
            new OptExecutor() { Base = baseToolchain.Executor })
    { }
}
class OptExecutor : IExecutor
{
    public required IExecutor Base { get; init; }

    readonly HashSet<string> _alreadyOptimizedPaths = new();

    public ExecuteResult Execute(ExecuteParameters pars)
    {
        string binaryDir = pars.BuildResult.ArtifactsPaths.BinariesDirectoryPath;
        string benchAsmExe = Path.GetFileName(pars.BenchmarkCase.Descriptor.Type.Assembly.Location);

        if (pars.BuildResult.IsBuildSuccess && _alreadyOptimizedPaths.Add(binaryDir)) {
            using var proc = Process.Start(new ProcessStartInfo() {
                FileName = "dotnet",
                ArgumentList = {
                    Path.Combine(AppContext.BaseDirectory, "DistIL.Cli.dll"),
                    "-i", Path.Combine(binaryDir, benchAsmExe)
                },
                RedirectStandardOutput = true
            })!;
            var stdout = proc.StandardOutput;

            while (stdout.ReadLine() is string ln) {
                pars.Logger.WriteLine(LogKind.Info, ln);
            }
            proc.WaitForExit();
        }
        return Base.Execute(pars);
    }
}