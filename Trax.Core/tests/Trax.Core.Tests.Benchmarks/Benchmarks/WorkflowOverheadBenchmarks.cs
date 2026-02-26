using BenchmarkDotNet.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Core.Tests.Benchmarks.Models;
using Trax.Core.Tests.Benchmarks.Serial;
using Trax.Core.Tests.Benchmarks.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Core.Tests.Benchmarks.Benchmarks;

/// <summary>
/// Compares the overhead of different execution modes for the same workloads:
/// Serial (plain function) vs Base Workflow vs EffectWorkflow (no effects) vs EffectWorkflow (InMemory).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class WorkflowOverheadBenchmarks
{
    private ServiceProvider _noEffectsProvider = null!;
    private ServiceProvider _inMemoryProvider = null!;

    private readonly PersonDto _samplePerson = new("John", "Doe", 30, "john@example.com");

    [GlobalSetup]
    public void Setup()
    {
        // EffectWorkflow with no effect providers
        var noEffectsServices = new ServiceCollection();
        noEffectsServices.AddTraxEffects();
        noEffectsServices.AddScopedTraxRoute<IEffectAddOneWorkflow, EffectAddOneWorkflow>();
        noEffectsServices.AddScopedTraxRoute<
            IEffectAddThreeWorkflow,
            EffectAddThreeWorkflow
        >();
        noEffectsServices.AddScopedTraxRoute<
            IEffectTransformWorkflow,
            EffectTransformWorkflow
        >();
        noEffectsServices.AddScopedTraxRoute<
            IEffectSimulatedIoWorkflow,
            EffectSimulatedIoWorkflow
        >();
        _noEffectsProvider = noEffectsServices.BuildServiceProvider();

        // EffectWorkflow with InMemory effect
        var inMemoryServices = new ServiceCollection();
        inMemoryServices.AddTraxEffects(options => options.AddInMemoryEffect());
        inMemoryServices.AddScopedTraxRoute<IEffectAddOneWorkflow, EffectAddOneWorkflow>();
        inMemoryServices.AddScopedTraxRoute<
            IEffectAddThreeWorkflow,
            EffectAddThreeWorkflow
        >();
        inMemoryServices.AddScopedTraxRoute<
            IEffectTransformWorkflow,
            EffectTransformWorkflow
        >();
        inMemoryServices.AddScopedTraxRoute<
            IEffectSimulatedIoWorkflow,
            EffectSimulatedIoWorkflow
        >();
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

    [Benchmark(Description = "BaseWorkflow_Add1")]
    public Task<int> BaseWorkflow_AddOne() => new AddOneWorkflow().Run(0);

    [Benchmark(Description = "EffectWorkflow_NoEffects_Add1")]
    public async Task<int> EffectWorkflow_NoEffects_AddOne()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectAddOneWorkflow>();
        return await workflow.Run(0);
    }

    [Benchmark(Description = "EffectWorkflow_InMemory_Add1")]
    public async Task<int> EffectWorkflow_InMemory_AddOne()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectAddOneWorkflow>();
        return await workflow.Run(0);
    }

    // ===== Arithmetic: Add 3 (three steps) =====

    [Benchmark(Description = "Serial_Add3")]
    public Task<int> Serial_AddThree() => SerialOperations.AddThreeSerial(0);

    [Benchmark(Description = "BaseWorkflow_Add3")]
    public Task<int> BaseWorkflow_AddThree() => new AddThreeWorkflow().Run(0);

    [Benchmark(Description = "EffectWorkflow_NoEffects_Add3")]
    public async Task<int> EffectWorkflow_NoEffects_AddThree()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectAddThreeWorkflow>();
        return await workflow.Run(0);
    }

    [Benchmark(Description = "EffectWorkflow_InMemory_Add3")]
    public async Task<int> EffectWorkflow_InMemory_AddThree()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectAddThreeWorkflow>();
        return await workflow.Run(0);
    }

    // ===== Object Transformation =====

    [Benchmark(Description = "Serial_Transform")]
    public Task<PersonEntity> Serial_Transform() => SerialOperations.TransformSerial(_samplePerson);

    [Benchmark(Description = "BaseWorkflow_Transform")]
    public Task<PersonEntity> BaseWorkflow_Transform() =>
        new TransformWorkflow().Run(_samplePerson);

    [Benchmark(Description = "EffectWorkflow_NoEffects_Transform")]
    public async Task<PersonEntity> EffectWorkflow_NoEffects_Transform()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectTransformWorkflow>();
        return await workflow.Run(_samplePerson);
    }

    [Benchmark(Description = "EffectWorkflow_InMemory_Transform")]
    public async Task<PersonEntity> EffectWorkflow_InMemory_Transform()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectTransformWorkflow>();
        return await workflow.Run(_samplePerson);
    }

    // ===== Simulated I/O (3 Task.Yield steps) =====

    [Benchmark(Description = "Serial_SimulatedIO")]
    public Task<int> Serial_SimulatedIo() => SerialOperations.SimulatedIoSerial(0, 3);

    [Benchmark(Description = "BaseWorkflow_SimulatedIO")]
    public Task<int> BaseWorkflow_SimulatedIo() => new SimulatedIoWorkflow().Run(0);

    [Benchmark(Description = "EffectWorkflow_NoEffects_SimulatedIO")]
    public async Task<int> EffectWorkflow_NoEffects_SimulatedIo()
    {
        using var scope = _noEffectsProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectSimulatedIoWorkflow>();
        return await workflow.Run(0);
    }

    [Benchmark(Description = "EffectWorkflow_InMemory_SimulatedIO")]
    public async Task<int> EffectWorkflow_InMemory_SimulatedIo()
    {
        using var scope = _inMemoryProvider.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IEffectSimulatedIoWorkflow>();
        return await workflow.Run(0);
    }
}
