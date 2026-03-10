using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.Tests.Benchmarks.Serial;
using Trax.Effect.Tests.Benchmarks.Trains;

namespace Trax.Effect.Tests.Benchmarks.Benchmarks;

/// <summary>
/// Measures how overhead scales with step count.
/// Compares Serial vs Base Train vs EffectTrain (no effects) at 1, 3, 5, and 10 steps.
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
        services.AddTrax(trax => trax.AddEffects());
        services.AddScopedTraxRoute<IEffectAddOneX1Train, EffectAddOneX1Train>();
        services.AddScopedTraxRoute<IEffectAddOneX3Train, EffectAddOneX3Train>();
        services.AddScopedTraxRoute<IEffectAddOneX5Train, EffectAddOneX5Train>();
        services.AddScopedTraxRoute<IEffectAddOneX10Train, EffectAddOneX10Train>();
        _provider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark(Baseline = true, Description = "Serial")]
    public Task<int> Serial() => SerialOperations.AddNSerial(0, StepCount);

    [Benchmark(Description = "BaseTrain")]
    public Task<int> BaseTrain() =>
        StepCount switch
        {
            1 => new AddOneX1Train().Run(0),
            3 => new AddOneX3Train().Run(0),
            5 => new AddOneX5Train().Run(0),
            10 => new AddOneX10Train().Run(0),
            _ => throw new ArgumentOutOfRangeException(),
        };

    [Benchmark(Description = "EffectTrain_NoEffects")]
    public async Task<int> EffectTrain_NoEffects()
    {
        using var scope = _provider.CreateScope();
        return StepCount switch
        {
            1 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX1Train>().Run(0),
            3 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX3Train>().Run(0),
            5 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX5Train>().Run(0),
            10 => await scope.ServiceProvider.GetRequiredService<IEffectAddOneX10Train>().Run(0),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }
}
