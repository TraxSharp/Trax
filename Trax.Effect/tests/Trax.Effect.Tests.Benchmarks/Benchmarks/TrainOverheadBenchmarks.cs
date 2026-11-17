using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Tests.Benchmarks.Models;
using Trax.Effect.Tests.Benchmarks.Serial;
using Trax.Effect.Tests.Benchmarks.Trains;

namespace Trax.Effect.Tests.Benchmarks.Benchmarks;

/// <summary>
/// Compares the overhead of different execution modes for the same workloads:
/// Serial (plain function) vs Base Train vs EffectTrain (no effects) vs EffectTrain (InMemory).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class TrainOverheadBenchmarks
{
    private ServiceProvider? _noEffectsProvider;
    private ServiceProvider? _inMemoryProvider;

    private readonly PersonDto _samplePerson = new("John", "Doe", 30, "john@example.com");

    [GlobalSetup]
    public void Setup()
    {
        // EffectTrain with no effect providers
        var noEffectsServices = new ServiceCollection();
        noEffectsServices.AddTrax(trax => trax.AddEffects());
        noEffectsServices.AddScopedTraxRoute<IEffectAddOneTrain, EffectAddOneTrain>();
        noEffectsServices.AddScopedTraxRoute<IEffectAddThreeTrain, EffectAddThreeTrain>();
        noEffectsServices.AddScopedTraxRoute<IEffectTransformTrain, EffectTransformTrain>();
        noEffectsServices.AddScopedTraxRoute<IEffectSimulatedIoTrain, EffectSimulatedIoTrain>();
        _noEffectsProvider = noEffectsServices.BuildServiceProvider();

        // EffectTrain with InMemory effect
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        inMemoryServices.AddScopedTraxRoute<IEffectAddOneTrain, EffectAddOneTrain>();
        inMemoryServices.AddScopedTraxRoute<IEffectAddThreeTrain, EffectAddThreeTrain>();
        inMemoryServices.AddScopedTraxRoute<IEffectTransformTrain, EffectTransformTrain>();
        inMemoryServices.AddScopedTraxRoute<IEffectSimulatedIoTrain, EffectSimulatedIoTrain>();
        _inMemoryProvider = inMemoryServices.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _noEffectsProvider.NotNull().Dispose();
        _inMemoryProvider.NotNull().Dispose();
    }

    // ===== Arithmetic: Add 1 (single junction) =====

    [Benchmark(Baseline = true, Description = "Serial_Add1")]
    public static Task<int> Serial_AddOne() => SerialOperations.AddOneSerial(0);

    [Benchmark(Description = "BaseTrain_Add1")]
    public static Task<int> BaseTrain_AddOne() => new AddOneTrain().Run(0);

    [Benchmark(Description = "EffectTrain_NoEffects_Add1")]
    public async Task<int> EffectTrain_NoEffects_AddOne()
    {
        var scope = _noEffectsProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddOneTrain>();
        return await train.Run(0).ConfigureAwait(false);
    }

    [Benchmark(Description = "EffectTrain_InMemory_Add1")]
    public async Task<int> EffectTrain_InMemory_AddOne()
    {
        var scope = _inMemoryProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddOneTrain>();
        return await train.Run(0).ConfigureAwait(false);
    }

    // ===== Arithmetic: Add 3 (three steps) =====

    [Benchmark(Description = "Serial_Add3")]
    public static Task<int> Serial_AddThree() => SerialOperations.AddThreeSerial(0);

    [Benchmark(Description = "BaseTrain_Add3")]
    public static Task<int> BaseTrain_AddThree() => new AddThreeTrain().Run(0);

    [Benchmark(Description = "EffectTrain_NoEffects_Add3")]
    public async Task<int> EffectTrain_NoEffects_AddThree()
    {
        var scope = _noEffectsProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddThreeTrain>();
        return await train.Run(0).ConfigureAwait(false);
    }

    [Benchmark(Description = "EffectTrain_InMemory_Add3")]
    public async Task<int> EffectTrain_InMemory_AddThree()
    {
        var scope = _inMemoryProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddThreeTrain>();
        return await train.Run(0).ConfigureAwait(false);
    }

    // ===== Object Transformation =====

    [Benchmark(Description = "Serial_Transform")]
    public Task<PersonEntity> Serial_Transform() => SerialOperations.TransformSerial(_samplePerson);

    [Benchmark(Description = "BaseTrain_Transform")]
    public Task<PersonEntity> BaseTrain_Transform() => new TransformTrain().Run(_samplePerson);

    [Benchmark(Description = "EffectTrain_NoEffects_Transform")]
    public async Task<PersonEntity> EffectTrain_NoEffects_Transform()
    {
        var scope = _noEffectsProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectTransformTrain>();
        return await train.Run(_samplePerson).ConfigureAwait(false);
    }

    [Benchmark(Description = "EffectTrain_InMemory_Transform")]
    public async Task<PersonEntity> EffectTrain_InMemory_Transform()
    {
        var scope = _inMemoryProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectTransformTrain>();
        return await train.Run(_samplePerson).ConfigureAwait(false);
    }

    // ===== Simulated I/O (3 Task.Yield steps) =====

    [Benchmark(Description = "Serial_SimulatedIO")]
    public static Task<int> Serial_SimulatedIo() => SerialOperations.SimulatedIoSerial(0, 3);

    [Benchmark(Description = "BaseTrain_SimulatedIO")]
    public static Task<int> BaseTrain_SimulatedIo() => new SimulatedIoTrain().Run(0);

    [Benchmark(Description = "EffectTrain_NoEffects_SimulatedIO")]
    public async Task<int> EffectTrain_NoEffects_SimulatedIo()
    {
        var scope = _noEffectsProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectSimulatedIoTrain>();
        return await train.Run(0).ConfigureAwait(false);
    }

    [Benchmark(Description = "EffectTrain_InMemory_SimulatedIO")]
    public async Task<int> EffectTrain_InMemory_SimulatedIo()
    {
        var scope = _inMemoryProvider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = scope.ServiceProvider.GetRequiredService<IEffectSimulatedIoTrain>();
        return await train.Run(0).ConfigureAwait(false);
    }
}
