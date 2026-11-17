using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The ManifestManager resolves work queue entries a crash left unconfirmed in the middle of a
/// two-phase enqueue. Without it such an entry is never dispatched and never cleaned up.
///
/// <para>Enforces Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
[TestFixture]
public class ResolveStaleStagedEntriesJunctionTests : TestSetup
{
    private IManifestManagerTrain? _train;
    private SchedulerConfiguration? _config;

    public override async Task TestSetUp()
    {
        await base.TestSetUp().ConfigureAwait(false);
        _train = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        _config.NotNull().StaleStagedEntryTimeout = TimeSpan.FromMinutes(10);
        _config.NotNull().PromoteStaleStagedEntries = false;
    }

    [TearDown]
    public void ResolveStaleStagedEntriesJunctionTestsTearDown()
    {
        _config.NotNull().StaleStagedEntryTimeout =
            new SchedulerConfiguration().StaleStagedEntryTimeout;
        _config.NotNull().PromoteStaleStagedEntries = false;

        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    [Test]
    public async Task A_stale_staged_entry_is_cancelled_by_default()
    {
        var stale = await Stage(age: TimeSpan.FromMinutes(30));

        await _train.NotNull().Run(Unit.Default);

        (await Load(stale))
            .Status.Should()
            .Be(
                WorkQueueStatus.Cancelled,
                "nothing recorded says whether its hook succeeded, never ran, or rejected it"
            );
    }

    [Test]
    public async Task A_recent_staged_entry_is_left_alone()
    {
        var recent = await Stage(age: TimeSpan.FromMinutes(1));

        await _train.NotNull().Run(Unit.Default);

        var entry = await Load(recent);
        entry.Status.Should().Be(WorkQueueStatus.Queued, "its hook may still be running");
        entry.ConfirmedAt.Should().BeNull();
    }

    [Test]
    public async Task A_host_that_opts_in_promotes_stale_staged_entries()
    {
        _config.NotNull().PromoteStaleStagedEntries = true;
        var stale = await Stage(age: TimeSpan.FromMinutes(30));

        await _train.NotNull().Run(Unit.Default);

        var entry = await Load(stale);
        entry.Status.Should().Be(WorkQueueStatus.Queued);
        entry.ConfirmedAt.Should().NotBeNull();
    }

    private async Task<long> Stage(TimeSpan age)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName.NotNull(),
                Input = "{}",
                InputTypeName = typeof(SchedulerTestInput).FullName,
                DeferPromotion = true,
            },
            TimeProvider.System
        );
        entry.CreatedAt = TimeProvider.System.GetUtcNow().UtcDateTime - age;

        await DataContext.Track(entry).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        return entry.Id;
    }

    private async Task<WorkQueue> Load(long id)
    {
        DataContext.Reset();
        return await DataContext
            .WorkQueues.AsNoTracking()
            .FirstAsync(w => w.Id == id)
            .ConfigureAwait(false);
    }
}
