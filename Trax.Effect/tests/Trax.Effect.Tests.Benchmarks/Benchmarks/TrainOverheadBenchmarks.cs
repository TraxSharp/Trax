using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
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
    private ServiceProvider _noEffectsProvider = null!;
    private ServiceProvider _inMemoryProvider = null!;

    private readonly PersonDto _samplePerson = new("John", "Doe", 30, "john@example.com");

    [GlobalSetup]
    public void Setup()
    {
        // EffectTrain with no effect providers
        var noEffectsServices = new ServiceCollection();
        noEffectsServices.AddTraxEffects();
        noEffectsServices.AddScopedTraxRoute<IEffectAddOneTrain, EffectAddOneTrain>();
        noEffectsServices.AddScopedTraxRoute<IEffectAddThreeTrain, EffectAddThreeTrain>();
        noEffectsServices.AddScopedTraxRoute<IEffectTransformTrain, EffectTransformTrain>();
        noEffectsServices.AddScopedTraxRoute<IEffectSimulatedIoTrain, EffectSimulatedIoTrain>();
        _noEffectsProvider = noEffectsServices.BuildServiceProvider();

        // EffectTrain with InMemory effect
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddTraxEffects(options => options.AddInMemoryEffect());
        inMemoryServices.AddScopedTraxRoute<IEffectAddOneTrain, EffectAddOneTrain>();
        inMemoryServices.AddScopedTraxRoute<IEffectAddThreeTrain, EffectAddThreeTrain>();
        inMemoryServices.AddScopedTraxRoute<IEffectTransformTrain, EffectTransformTrain>();
        inMemoryServices.AddScopedTraxRoute<IEffectSimulatedIoTrain, EffectSimulatedIoTrain>();
        _inMemoryProvider = inMemoryServices.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _noEffectsProvider.Dispose();
        _inMemoryProvider.Dispose();
    }

    // ===== Arithmetic: Add 1 (single step) =====

    [Benchmark(Baseline = true, Description = "Serial_Add1")]
    public Task<int> Serial_AddOne() => SerialOperations.AddOneSerial(0);

    [Benchmark(Description = "BaseTrain_Add1")]
    public Task<int> BaseTrain_AddOne() => new AddOneTrain().Run(0);

    [Benchmark(Description = "EffectTrain_NoEffects_Add1")]
    public async Task<int> EffectTrain_NoEffects_AddOne()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddOneTrain>();
        return await train.Run(0);
    }

    [Benchmark(Description = "EffectTrain_InMemory_Add1")]
    public async Task<int> EffectTrain_InMemory_AddOne()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddOneTrain>();
        return await train.Run(0);
    }

    // ===== Arithmetic: Add 3 (three steps) =====

    [Benchmark(Description = "Serial_Add3")]
    public Task<int> Serial_AddThree() => SerialOperations.AddThreeSerial(0);

    [Benchmark(Description = "BaseTrain_Add3")]
    public Task<int> BaseTrain_AddThree() => new AddThreeTrain().Run(0);

    [Benchmark(Description = "EffectTrain_NoEffects_Add3")]
    public async Task<int> EffectTrain_NoEffects_AddThree()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddThreeTrain>();
        return await train.Run(0);
    }

    [Benchmark(Description = "EffectTrain_InMemory_Add3")]
    public async Task<int> EffectTrain_InMemory_AddThree()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectAddThreeTrain>();
        return await train.Run(0);
    }

    // ===== Object Transformation =====

    [Benchmark(Description = "Serial_Transform")]
    public Task<PersonEntity> Serial_Transform() => SerialOperations.TransformSerial(_samplePerson);

    [Benchmark(Description = "BaseTrain_Transform")]
    public Task<PersonEntity> BaseTrain_Transform() => new TransformTrain().Run(_samplePerson);

    [Benchmark(Description = "EffectTrain_NoEffects_Transform")]
    public async Task<PersonEntity> EffectTrain_NoEffects_Transform()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectTransformTrain>();
        return await train.Run(_samplePerson);
    }

    [Benchmark(Description = "EffectTrain_InMemory_Transform")]
    public async Task<PersonEntity> EffectTrain_InMemory_Transform()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectTransformTrain>();
        return await train.Run(_samplePerson);
    }

    // ===== Simulated I/O (3 Task.Yield steps) =====

    [Benchmark(Description = "Serial_SimulatedIO")]
    public Task<int> Serial_SimulatedIo() => SerialOperations.SimulatedIoSerial(0, 3);

    [Benchmark(Description = "BaseTrain_SimulatedIO")]
    public Task<int> BaseTrain_SimulatedIo() => new SimulatedIoTrain().Run(0);

    [Benchmark(Description = "EffectTrain_NoEffects_SimulatedIO")]
    public async Task<int> EffectTrain_NoEffects_SimulatedIo()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectSimulatedIoTrain>();
        return await train.Run(0);
    }

    [Benchmark(Description = "EffectTrain_InMemory_SimulatedIO")]
    public async Task<int> EffectTrain_InMemory_SimulatedIo()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IEffectSimulatedIoTrain>();
        return await train.Run(0);
    }
}
