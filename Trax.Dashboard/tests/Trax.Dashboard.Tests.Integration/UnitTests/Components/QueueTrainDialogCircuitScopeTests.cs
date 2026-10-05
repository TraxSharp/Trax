using AwesomeAssertions;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Core.Functional;
using Trax.Dashboard.Components.Dialogs;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// A Blazor circuit's scope lives as long as the operator's tab. Queueing a train that overrides
/// <c>OnQueue</c> or <c>QueueSubjectKey</c> resolves the train to ask it, and if that instance
/// were resolved from the circuit's scope it would stay there, with its effect runner and data
/// context, until the tab closed, one more for every queue. The enqueue resolves it in a scope of
/// its own and disposes it before returning, so however often the operator queues, the circuit
/// holds no train.
/// </summary>
[TestFixture]
public class QueueTrainDialogCircuitScopeTests
{
    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;
    private TrainLifetimes _lifetimes = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();
        _lifetimes = new TrainLifetimes();
        _ctx.Services.AddSingleton(_lifetimes);
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task Queueing_a_hooked_subject_keyed_train_five_times_leaves_no_train_in_the_circuit_scope()
    {
        var services = _ctx.Services;
        services.AddScopedTraxRoute<IHookedTrain, HookedTrain>();
        var discovery = new TrainDiscoveryService(services);
        services.AddRealOperationsService(discovery, _data);
        var registration = discovery
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(IHookedTrain));

        for (var i = 0; i < 5; i++)
        {
            var dialog = _ctx.RenderComponent<QueueTrainDialog>(p =>
                p.Add(x => x.Registration, registration)
            );
            dialog.Find("input.rz-textbox").Change($"customer-{i % 2}");
            await dialog
                .FindAll("button")
                .Single(b => b.TextContent.Contains("Queue"))
                .ClickAsync(new());
            dialog.FindAll(".rz-alert").Select(a => a.TextContent.Trim()).Should().BeEmpty();
        }

        await using (var db = await _data.CreateDbContextAsync(default))
            (await db.WorkQueues.CountAsync()).Should().Be(5);

        _lifetimes.OnQueueCalls.Should().Be(5, "every queue asked the train's hook");
        _lifetimes
            .Constructed.Should()
            .BeGreaterThanOrEqualTo(5, "the train is resolved to ask its subject key and hook");
        _lifetimes
            .Disposed.Should()
            .Be(
                _lifetimes.Constructed,
                "every train an enqueue resolved was disposed with the enqueue's own scope, so "
                    + "the circuit's scope, which lives until the tab closes, holds none"
            );
    }

    public sealed class TrainLifetimes
    {
        public int Constructed;
        public int Disposed;
        public int OnQueueCalls;
    }

    public record CustomerInput
    {
        public string Customer { get; init; } = "";
    }

    public interface IHookedTrain : IServiceTrain<CustomerInput, Unit> { }

    public class HookedTrain : ServiceTrain<CustomerInput, Unit>, IHookedTrain, IDisposable
    {
        private readonly TrainLifetimes _lifetimes;

        public HookedTrain(TrainLifetimes lifetimes)
        {
            _lifetimes = lifetimes;
            Interlocked.Increment(ref lifetimes.Constructed);
        }

        protected override string? QueueSubjectKey(Metadata metadata) =>
            metadata.GetInput<CustomerInput>()?.Customer;

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Interlocked.Increment(ref _lifetimes.OnQueueCalls);
            return Task.CompletedTask;
        }

        private int _disposed;

        // The route registers the train under its interface and its own type, so a scope ends by
        // disposing the one instance twice; it is counted once.
        void IDisposable.Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Increment(ref _lifetimes.Disposed);
            Dispose();
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
