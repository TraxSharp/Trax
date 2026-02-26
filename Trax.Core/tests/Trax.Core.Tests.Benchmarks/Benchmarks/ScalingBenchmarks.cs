using BenchmarkDotNet.Attributes;
using Trax.Effect.Extensions;
using Trax.Core.Tests.Benchmarks.Serial;
using Trax.Core.Tests.Benchmarks.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Core.Tests.Benchmarks.Benchmarks;

/// <summary>
/// Measures how overhead scales with step count.
/// Compares Serial vs Base Workflow vs EffectWorkflow (no effects) at 1, 3, 5, and 10 steps.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ScalingBenchmarks
{
    private ServiceProvider _provider = null!;

    [Params(1, 3, 5, 10)]
    public int StepCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddTraxEffects();
        services.AddScopedTraxRoute<IEffectAddOneX1Workflow, EffectAddOneX1Workflow>();
        services.AddScopedTraxRoute<IEffectAddOneX3Workflow, EffectAddOneX3Workflow>();
        services.AddScopedTraxRoute<IEffectAddOneX5Workflow, EffectAddOneX5Workflow>();
        services.AddScopedTraxRoute<IEffectAddOneX10Workflow, EffectAddOneX10Workflow>();
        _provider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark(Baseline = true, Description = "Serial")]
    public Task<int> Serial() => SerialOperations.AddNSerial(0, StepCount);

    [Benchmark(Description = "BaseWorkflow")]
    public Task<int> BaseWorkflow() =>
        StepCount switch
        {
            1 => new AddOneX1Workflow().Run(0),
            3 => new AddOneX3Workflow().Run(0),
            5 => new AddOneX5Workflow().Run(0),
            10 => new AddOneX10Workflow().Run(0),
            _ => throw new ArgumentOutOfRangeException()
        };

    [Benchmark(Description = "EffectWorkflow_NoEffects")]
    public async Task<int> EffectWorkflow_NoEffects()
    {
        using var scope = _provider.CreateScope();
        return StepCount switch
        {
            1 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX1Workflow>().Run(0),
            3 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX3Workflow>().Run(0),
            5 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX5Workflow>().Run(0),
            10 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX10Workflow>().Run(0),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
