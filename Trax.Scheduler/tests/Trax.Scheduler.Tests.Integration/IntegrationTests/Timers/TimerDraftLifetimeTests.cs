using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.Timers;

/// <summary>
/// A pending timer keeps a user's draft alive past its idle TTL until the timer fires, and the TTL counts again
/// from the fire; but nothing keeps a draft past the configured maximum lifetime, counted from its creation. The
/// hosts here configure both bounds explicitly; expiry is the draft sweep, run here as a step. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TimerDraftLifetimeTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(1);

    private InvokeCluster? _cluster;
    private TimerDb? _db;

    // Drafts expire after an hour idle and a day after they were created.
    private ClusterHost? _api;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _db = new TimerDb(_cluster.NotNull());
        _api = _cluster
            .NotNull()
            .Host(
                machines: true,
                scheduler: false,
                TimerHosts.Publishing,
                options: o =>
                {
                    o.DraftTtl = Ttl;
                    o.MaxDraftLifetime = MaxLifetime;
                }
            );
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        if (_api is not null)
            await _api.DisposeAsync();
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [SetUp]
    public Task Reset() => _cluster.NotNull().Reset();

    [Test]
    public async Task A_draft_past_its_TTL_with_a_pending_timer_survives_until_it_fires()
    {
        var (user, id, row) = await OpenReview();
        await _db.NotNull().DueIn(row, TimeSpan.FromMinutes(30));
        await _api.NotNull().IdleDraft(row, Ttl * 2);
        await _api.NotNull().ExpireDrafts();

        (await _api.NotNull().Load(user, id))
            .Should()
            .BeOfType<LoadResult.Loaded>(
                $"a draft whose timer is still pending is kept past its idle TTL. See {Adr}"
            );

        await _db.NotNull().MakeDue(row);
        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);
        (await Reminder(id)).State.Should().Be(nameof(ReminderState.Expired));
    }

    [Test]
    public async Task A_pending_timer_never_keeps_a_draft_past_MaxDraftLifetime()
    {
        var (user, id, row) = await OpenReview();
        await _db.NotNull().DueIn(row, TimeSpan.FromMinutes(30));
        await _api.NotNull().AgeDraft(row, MaxLifetime + TimeSpan.FromHours(1));
        await _api.NotNull().IdleDraft(row, Ttl * 2);
        await _api.NotNull().ExpireDrafts();

        (await _api.NotNull().Load(user, id))
            .Should()
            .BeOfType<LoadResult.NotFound>(
                $"the maximum lifetime bounds a draft however its timers extend it. See {Adr}"
            );
        (await _api.NotNull().Rows(id, ReminderMachine.MachineId)).Should().BeEmpty();
    }

    [Test]
    public async Task The_TTL_sweeper_skips_a_future_due_at_and_counts_from_the_fire()
    {
        var (user, id, row) = await OpenReview();
        await _db.NotNull().DueIn(row, TimeSpan.FromMinutes(30));
        await _api.NotNull().IdleDraft(row, Ttl * 2);
        await _api.NotNull().ExpireDrafts();
        (await _api.NotNull().Load(user, id))
            .Should()
            .BeOfType<LoadResult.Loaded>("the timer is still pending");

        await _db.NotNull().MakeDue(row);
        (await _api.NotNull().FireTimers())
            .Should()
            .ContainSingle(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired);

        await _api.NotNull().IdleDraft(row, Ttl / 2);
        await _api.NotNull().ExpireDrafts();
        (await _api.NotNull().Load(user, id))
            .Should()
            .BeOfType<LoadResult.Loaded>(
                $"the TTL counts from the fire, which was a write. See {Adr}"
            );

        await _api.NotNull().IdleDraft(row, Ttl * 2);
        await _api.NotNull().ExpireDrafts();
        (await _api.NotNull().Load(user, id))
            .Should()
            .BeOfType<LoadResult.NotFound>("idle past its TTL with no timer pending, it expires");
    }

    private async Task<(string User, Guid Id, long Row)> OpenReview()
    {
        var user = "user-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        (await _api.NotNull().OpenReview(user, id).ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        return (user, id, (await Reminder(id).ConfigureAwait(false)).RowId);
    }

    private async Task<SnapshotDraft> Reminder(Guid id) =>
        (await _api.NotNull().Row(id, ReminderMachine.MachineId).ConfigureAwait(false)).NotNull();
}
