using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// What the outcome reconciler reads and delivers: a sweep pages through only the rows whose run has ended, on the
/// machines its host handles; a batch of notices is deduplicated and filtered the same way; and a row the host
/// cannot read waits longer each time before it is tried again. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class InvokeOutcomeReconcilerTests(ClusterStore store)
{
    private InvokeCluster? _cluster;
    private ClusterHost? _api;
    private ClusterHost? _worker;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _api = _cluster.NotNull().Host(machines: true, scheduler: false);
        _worker = _cluster.NotNull().Host(machines: false, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        if (_api is not null)
            await _api.DisposeAsync();
        if (_worker is not null)
            await _worker.DisposeAsync();
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.NotNull().Reset();
    }

    [TearDown]
    public void Restore()
    {
        InvokedStepGate.Release();
        _api.NotNull().Reconciler.PageSize = 200;
        _api.NotNull().Reconciler.Time = TimeProvider.System;
    }

    [Test]
    public async Task The_sweep_pages_through_the_ended_runs_and_reads_none_still_going()
    {
        var ended = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var token = await StartedToken();
            await _worker.NotNull().DispatchAndRun(token);
            ended.Add(token);
        }

        // Still going: one queued, one dispatched and not yet run.
        var queued = await StartedToken();
        var dispatched = await StartedToken();
        await _worker.NotNull().Dispatch(dispatched);

        var pages = new List<EndedPage>();
        var scope = _api.NotNull().Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var delivery = scope.ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>();
            string? after = null;
            do
            {
                var page = await delivery.Ended([SystemStepMachine.MachineId], 2, after);
                pages.Add(page);
                after = page.Next;
            } while (after is not null);

            (await delivery.Ended([UserStepMachine.MachineId], 10))
                .Tokens.Should()
                .BeEmpty("only the machines named are read");
        }

        pages.Should().HaveCountGreaterThan(2, "five ended runs at two a page");
        pages
            .SelectMany(p => p.Tokens)
            .Should()
            .Equal(
                ended.Order(StringComparer.Ordinal),
                "each ended run once, in token order, and none still going"
            );

        _api.NotNull().Reconciler.PageSize = 2;
        var swept = await _api.NotNull().Sweep();

        swept.Should().HaveCount(5).And.AllBeOfType<InvokeDelivery.Moved>();
        (await RowHolding(queued)).Should().NotBeNull("a queued run has not ended");
        (await RowHolding(dispatched)).Should().NotBeNull("a dispatched run has not ended");
    }

    [Test]
    public async Task A_batch_of_notices_delivers_each_ended_run_once()
    {
        var ended = await StartedToken();
        await _worker.NotNull().DispatchAndRun(ended);
        var queued = await StartedToken();

        var deliveries = new ConcurrentBag<string>();
        void Count(object? sender, InvokeDeliveredEventArgs e) => deliveries.Add(e.InvokeToken);
        _api.NotNull().Reconciler.Delivered += Count;
        try
        {
            var results = await _api.NotNull()
                .Reconciler.DeliverAmong(
                    [ended, queued, ended, "no-such-run", ended],
                    CancellationToken.None
                );

            results
                .Should()
                .ContainSingle()
                .Which.Should()
                .BeEquivalentTo(new { To = "Done", Applied = "done" });
        }
        finally
        {
            _api.NotNull().Reconciler.Delivered -= Count;
        }

        deliveries
            .Should()
            .Equal(
                [ended],
                "a run named three times is delivered once, and one still going or unknown is not delivered at all"
            );
        (await RowHolding(queued)).Should().NotBeNull();
    }

    [Test]
    public async Task A_row_this_host_cannot_read_is_tried_again_only_after_a_growing_wait()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        _api.NotNull().Reconciler.Time = time;
        var token = await StartedToken();
        await _worker.NotNull().DispatchAndRun(token);

        // Written by a newer definition than this host's: it cannot read the snapshot.
        await SetVersion(token, 2);

        (await _api.NotNull().Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<InvokeDelivery.NotHere>();
        (await _api.NotNull().Sweep())
            .Should()
            .BeEmpty("the row waits before it is tried, and its failure logged, again");
        (await _api.NotNull().Reconciler.DeliverAmong([token], CancellationToken.None))
            .Should()
            .BeEmpty("a notice does not cut the wait short");

        time.Advance(InvokeOutcomeReconciler.NotHereBackoff + TimeSpan.FromSeconds(1));
        (await _api.NotNull().Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<InvokeDelivery.NotHere>();

        time.Advance(InvokeOutcomeReconciler.NotHereBackoff + TimeSpan.FromSeconds(1));
        (await _api.NotNull().Sweep()).Should().BeEmpty("the second wait is twice the first");

        // A host that can read it again applies the outcome once its wait is over.
        await SetVersion(token, 1);
        time.Advance(InvokeOutcomeReconciler.NotHereBackoffCap);
        (await _api.NotNull().Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done", Applied = "done" });
    }

    private async Task<string> StartedToken()
    {
        var instance = await _api.NotNull()
            .Start(StepMachine.Context(InvokedStepModes.Ok))
            .ConfigureAwait(false);
        return (
            await _api.NotNull().Row(instance.Id, SystemStepMachine.MachineId).ConfigureAwait(false)
        )
            .NotNull()
            .InvokeToken.NotNull();
    }

    private async Task<SnapshotDraft?> RowHolding(string token)
    {
        var scope = _api.NotNull().Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .SingleOrDefaultAsync(d => d.InvokeToken == token)
            .ConfigureAwait(false);
    }

    private async Task SetVersion(string token, int version)
    {
        var scope = _api.NotNull().Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.Where(d => d.InvokeToken == token)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Version, version))
                .ConfigureAwait(false)
        )
            .Should()
            .Be(1);
    }
}
