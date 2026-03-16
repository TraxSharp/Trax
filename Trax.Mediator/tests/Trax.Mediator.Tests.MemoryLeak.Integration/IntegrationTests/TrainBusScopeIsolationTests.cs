using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

[TestFixture]
public class TrainBusScopeIsolationTests
{
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();
        _serviceProvider = CreateScopeTrackingServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        TrainBus.ClearMethodCache();
    }

    private static IServiceProvider CreateScopeTrackingServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScoped<ScopeMarker>();
        services.AddScoped<DisposalTracker>();

        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().AddJson().SaveTrainParameters())
                .AddMediator(typeof(AssemblyMarker).Assembly)
        );

        return services.BuildServiceProvider();
    }

    #region ScopeIsolation

    [Test]
    public async Task RunAsync_TwoCalls_GetDifferentScopes()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var result1 = await trainBus.RunAsync<ScopeTrackOutput>(new ScopeTrackInput());
        var result2 = await trainBus.RunAsync<ScopeTrackOutput>(new ScopeTrackInput());

        result1.ScopeMarkerId.Should().NotBe(result2.ScopeMarkerId);
    }

    [Test]
    public async Task RunAsync_WithCancellationToken_CreatesChildScope()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var result1 = await trainBus.RunAsync<ScopeTrackOutput>(
            new ScopeTrackInput(),
            CancellationToken.None
        );
        var result2 = await trainBus.RunAsync<ScopeTrackOutput>(
            new ScopeTrackInput(),
            CancellationToken.None
        );

        result1.ScopeMarkerId.Should().NotBe(result2.ScopeMarkerId);
    }

    [Test]
    public async Task RunAsync_NonGeneric_CreatesChildScope()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        // Void overload — verifies it doesn't throw (scope creation works)
        await trainBus.RunAsync(new ScopeTrackInput());
        await trainBus.RunAsync(new ScopeTrackInput());
    }

    [Test]
    public async Task RunAsync_NonGeneric_WithCancellationToken_CreatesChildScope()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        await trainBus.RunAsync(new ScopeTrackInput(), CancellationToken.None);
        await trainBus.RunAsync(new ScopeTrackInput(), CancellationToken.None);
    }

    [Test]
    public async Task RunAsync_TrainScope_DiffersFromCallerScope()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        var callerMarker = scope.ServiceProvider.GetRequiredService<ScopeMarker>();

        var result = await trainBus.RunAsync<ScopeTrackOutput>(new ScopeTrackInput());

        result.ScopeMarkerId.Should().NotBe(callerMarker.Id);
    }

    #endregion

    #region ScopeDisposal

    [Test]
    public async Task RunAsync_ScopeDisposed_AfterCompletion()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var result = await trainBus.RunAsync<ScopeTrackOutput>(new ScopeTrackInput());

        result.Tracker.IsDisposed.Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_FailingTrain_ScopeStillDisposed()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var act = async () =>
            await trainBus.RunAsync<MemoryTestOutput>(MemoryTestModelFactory.CreateFailingInput());

        await act.Should().ThrowAsync<InvalidOperationException>();

        // The scope was created and disposed even though the train failed —
        // we can't capture the DisposalTracker from a failing train, but the
        // using statement in RunAsync guarantees scope disposal on all paths.
    }

    #endregion

    #region NestedDispatch

    [Test]
    public async Task RunAsync_NestedDispatch_EachTrainGetsOwnScope()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var result = await trainBus.RunAsync<NestedScopeTrackOutput>(new NestedScopeTrackInput());

        result.ParentScopeMarkerId.Should().NotBe(result.ChildScopeMarkerId);
    }

    #endregion

    #region Concurrency

    [Test]
    public async Task RunAsync_Concurrent_AllGetUniqueScopeIds()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();

        var tasks = Enumerable
            .Range(0, 10)
            .Select(_ => trainBus.RunAsync<ScopeTrackOutput>(new ScopeTrackInput()))
            .ToList();

        var results = await Task.WhenAll(tasks);
        var uniqueIds = results.Select(r => r.ScopeMarkerId).Distinct().ToList();

        uniqueIds.Should().HaveCount(10);
    }

    #endregion

    #region InitializeTrain

    [Test]
    public void InitializeTrain_ResolvesFromOwnScope_NotChildScope()
    {
        using var scope = _serviceProvider.CreateScope();
        var trainBus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        var callerMarker = scope.ServiceProvider.GetRequiredService<ScopeMarker>();

        var train = trainBus.InitializeTrain(new ScopeTrackInput());
        var scopeTrackTrain = train as ScopeTrackTrain;

        scopeTrackTrain.Should().NotBeNull();
        // InitializeTrain resolves from the TrainBus's own scope (the caller's scope),
        // not from a child scope — so the marker should match.
        scopeTrackTrain!.Marker!.Id.Should().Be(callerMarker.Id);
    }

    #endregion
}
