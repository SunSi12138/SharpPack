using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

var config = DefaultConfig.Instance
    .AddJob(Job.Default
        .WithId("Release")
        .WithLaunchCount(3)
        .WithWarmupCount(4)
        .WithIterationCount(12))
    .AddColumn(StatisticColumn.OperationsPerSecond);

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args, config);
