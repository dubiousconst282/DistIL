using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

var config = DefaultConfig.Instance
    .AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByMethod, BenchmarkLogicalGroupRule.ByParams)
    .AddJob(Job.ShortRun
        .WithIterationCount(20)
        .AsBaseline())
    .AddJob(Job.ShortRun
        .WithIterationCount(20)
        .WithArguments(new[] { new MsBuildArgument("/p:RunDistil=true") }));

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);
