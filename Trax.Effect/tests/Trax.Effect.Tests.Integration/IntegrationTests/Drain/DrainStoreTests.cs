using System.Globalization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.Drain;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests.Drain;

/// <summary>
/// The <c>trax.drain</c> row on Postgres (Trax.Docs/adr/0065): it is set, replaced, read back while it holds, ignored
/// once it lapses, and deleted; and what a drain waits on is the runs that are live and the instances not at rest.
/// </summary>
[TestFixture]
[NonParallelizable]
[LeavesStuckRuns("Seeds in-progress runs no host is running, to read them back as busy.")]
public class DrainStoreTests : TestSetup
{
    private const string Machine = "drain-store-tests";
    private const string Train = "Drain.Tests.ITrain";
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private IDataContext? _db;

    private IDataContext Db => _db.NotNull();

    [SetUp]
    public async Task OpenContext()
    {
        _db = (IDataContext)DataContextFactory.Create();
        await Db.Drains.ExecuteDeleteAsync();
        await Db.SnapshotDrafts.Where(d => d.Machine == Machine).ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task CloseContext()
    {
        await Db.Drains.ExecuteDeleteAsync();
        await Db.SnapshotDrafts.Where(d => d.Machine == Machine).ExecuteDeleteAsync();
        await _db.NotNull().DisposeAsync();
        _db = null;
    }

    [Test]
    public async Task A_drain_holds_until_its_time_and_not_after()
    {
        await DrainStore.SetAsync(Db, Now.AddMinutes(20), Now, "deploy", CancellationToken.None);
        Db.Reset();

        var held = await DrainStore.ReadAsync(Db, Now.AddMinutes(19), CancellationToken.None);
        held.Should().NotBeNull();
        held.NotNull().StartedBy.Should().Be("deploy");
        (await DrainStore.IsDrainedAsync(Db, Now.AddMinutes(20), CancellationToken.None))
            .Should()
            .BeFalse("a drain lapses at its time, so a deploy that died stops pausing the hosts");
    }

    [Test]
    public async Task Setting_a_drain_again_replaces_the_one_row()
    {
        await DrainStore.SetAsync(Db, Now.AddMinutes(5), Now, "deploy", CancellationToken.None);
        Db.Reset();
        await DrainStore.SetAsync(
            Db,
            Now.AddMinutes(30),
            Now.AddMinutes(1),
            "operator:bob",
            CancellationToken.None
        );
        Db.Reset();

        var rows = await Db.Drains.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].StartedBy.Should().Be("operator:bob");
        DateTime
            .SpecifyKind(rows[0].Until, DateTimeKind.Utc)
            .Should()
            .Be(Now.AddMinutes(30).UtcDateTime);
    }

    [Test]
    public async Task The_database_refuses_a_second_row() =>
        await FluentActions
            .Awaiting(async () =>
                await ((DbContext)Db)
                    .Database.ExecuteSqlRawAsync(
                        "INSERT INTO trax.drain (id, until, started_at, started_by) VALUES (2, now(), now(), 'x')"
                    )
                    .ConfigureAwait(false)
            )
            .Should()
            .ThrowAsync<Exception>();

    [Test]
    public async Task Clearing_reports_whether_a_drain_held_and_deletes_a_lapsed_one_too()
    {
        await DrainStore.SetAsync(
            Db,
            Now.AddMinutes(-1),
            Now.AddMinutes(-30),
            "deploy",
            CancellationToken.None
        );
        Db.Reset();

        (await DrainStore.ClearAsync(Db, Now, CancellationToken.None)).Should().BeFalse();
        Db.Reset();
        (await Db.Drains.CountAsync()).Should().Be(0);
        (await DrainStore.ClearAsync(Db, Now, CancellationToken.None)).Should().BeFalse();
    }

    [Test]
    public async Task An_instance_is_busy_while_an_effect_is_in_flight_or_its_invoked_run_is_live()
    {
        var live = await Run(TrainState.InProgress);
        var queued = await Run(TrainState.Pending);
        await Job(queued.Id, fetchedAt: null);
        var finished = await Run(TrainState.Completed);

        var effect = await Instance("Charging", effectToken: Guid.NewGuid(), invokeToken: null);
        var invoking = await Instance("Shipping", effectToken: null, invokeToken: live.ExternalId);
        await Instance("Waiting", effectToken: null, invokeToken: queued.ExternalId);
        await Instance("Done", effectToken: null, invokeToken: finished.ExternalId);
        await Instance("Idle", effectToken: null, invokeToken: null);

        var (items, total) = await DrainStore.BusyAsync(Db, [], CancellationToken.None);

        var instances = items
            .Where(i => string.Equals(i.Kind, DrainBusyItem.Instance, StringComparison.Ordinal))
            .ToList();
        instances
            .Select(i => i.Id)
            .Should()
            .BeEquivalentTo(
                [$"{Machine}/{effect.Id}", $"{Machine}/{invoking.Id}"],
                "an instance waiting on a run still in the queue, or on one that ended, is at rest"
            );
        instances
            .Single(i => i.Id.EndsWith(effect.Id.ToString(), StringComparison.Ordinal))
            .State.Should()
            .Contain("effect");
        items
            .Where(i => string.Equals(i.Kind, DrainBusyItem.Run, StringComparison.Ordinal))
            .Select(i => i.Id)
            .Should()
            .Equal(live.Id.ToString(CultureInfo.InvariantCulture));
        total.Should().Be(3);
    }

    [Test]
    public async Task The_list_is_capped_and_the_total_counts_past_it()
    {
        for (var i = 0; i < DrainStore.BusyListCap + 10; i++)
            await Run(TrainState.InProgress);

        var (items, total) = await DrainStore.BusyAsync(Db, [], CancellationToken.None);

        items.Should().HaveCount(DrainStore.BusyListCap);
        total.Should().Be(DrainStore.BusyListCap + 10);
        DrainCommand
            .BusyList(items, total)
            .Should()
            .EndWith("and 10 more", "a refusal says how many it did not list");
    }

    [Test]
    public async Task Ignored_trains_are_not_busy()
    {
        await Run(TrainState.InProgress, name: "Scheduler.IManifestManagerTrain");

        var (_, total) = await DrainStore.BusyAsync(
            Db,
            ["Scheduler.IManifestManagerTrain"],
            CancellationToken.None
        );

        total.Should().Be(0);
    }

    private async Task<Metadata> Run(TrainState state, string name = Train)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            },
            TimeProvider.System
        );
        metadata.TrainState = state;
        await Db.Track(metadata).ConfigureAwait(false);
        await Db.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        Db.Reset();
        return metadata;
    }

    private async Task Job(long metadataId, DateTime? fetchedAt)
    {
        var job = BackgroundJob.Create(
            new CreateBackgroundJob { MetadataId = metadataId },
            TimeProvider.System
        );
        job.FetchedAt = fetchedAt;
        await Db.Track(job).ConfigureAwait(false);
        await Db.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        Db.Reset();
    }

    private async Task<SnapshotDraft> Instance(string state, Guid? effectToken, string? invokeToken)
    {
        var row = new SnapshotDraft
        {
            Id = Guid.NewGuid(),
            OwnerKind = SnapshotOwnerKind.System,
            Machine = Machine,
            Version = 1,
            State = state,
            ConcurrencyToken = Guid.NewGuid(),
            UpdatedAt = TimeProvider.System.GetUtcNow(),
            EffectToken = effectToken,
            EffectTrigger = effectToken is null ? null : "Charge",
            InvokeToken = invokeToken,
        };
        Db.SnapshotDrafts.Add(row);
        await Db.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        Db.Reset();
        return row;
    }
}
