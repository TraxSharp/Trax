using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// An enqueue resolves the train it asks for its subject key, its deferral and its hook in a
/// scope of its own, once, and disposes it before returning. The caller's scope can live as
/// long as a Blazor circuit, and every train resolved from it stays referenced until it closes.
///
/// <para>Enforces <c>docs/adr/0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md")]
public class EnqueueScopeTests
{
    private ServiceProvider _serviceProvider = null!;
    private EnqueueScopeProbe _probe = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<EnqueueScopeProbe>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(EnqueueScopeTests).Assembly])
        );

        _serviceProvider = services.BuildServiceProvider();
        _probe = _serviceProvider.GetRequiredService<EnqueueScopeProbe>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        TrainBus.ClearMethodCache();
    }

    [Test]
    public async Task A_long_lived_scope_retains_no_train_an_enqueue_resolved()
    {
        const int enqueues = 5;
        using var circuit = _serviceProvider.CreateScope();
        var execution = circuit.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        for (var i = 0; i < enqueues; i++)
            await execution.QueueAsync(typeof(IKeyedHookedTrain).FullName!, "{}");

        _probe
            .Created.Should()
            .Be(
                enqueues,
                "the key, the deferral flag and the hook are read from one instance per enqueue"
            );
        _probe
            .Disposed.Should()
            .Be(
                _probe.Created,
                "each enqueue disposes the train it resolved, instead of leaving it to a caller "
                    + "scope that may live as long as a browser tab (see "
                    + "docs/adr/0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md)"
            );
    }

    #region Test infrastructure

    public class EnqueueScopeProbe
    {
        private int _created;
        private int _disposed;

        public int Created => _created;
        public int Disposed => _disposed;

        public void MarkCreated() => Interlocked.Increment(ref _created);

        public void MarkDisposed() => Interlocked.Increment(ref _disposed);
    }

    public record KeyedHookedInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IKeyedHookedTrain : IServiceTrain<KeyedHookedInput, Unit>;

    public class KeyedHookedTrain
        : ServiceTrain<KeyedHookedInput, Unit>,
            IKeyedHookedTrain,
            IDisposable
    {
        private readonly EnqueueScopeProbe _probe;
        private int _disposed;

        public KeyedHookedTrain(EnqueueScopeProbe probe)
        {
            _probe = probe;
            probe.MarkCreated();
        }

        protected override string? QueueSubjectKey(Metadata metadata) => "scope-probe";

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());

        // Counted once per instance: Dispose may be called more than once.
        void IDisposable.Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _probe.MarkDisposed();

            Dispose();
        }
    }

    #endregion
}
