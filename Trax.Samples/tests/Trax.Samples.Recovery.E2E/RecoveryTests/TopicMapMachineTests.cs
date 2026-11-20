using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The <c>topic-map</c> wizard, a user's own machine, driven over GraphQL as the page drives it: start it,
/// choose the fields, choose the years, build. <c>Building</c> runs the topic map train, and only that run's
/// outcome moves the draft on; a client cannot put the draft in <c>Built</c> itself or fire the
/// outcome by hand.
/// </summary>
[TestFixture]
public class TopicMapMachineTests : RecoveryTestFixture
{
    private TopicMapDraft? _draft;

    [SetUp]
    public void NewDraft() => _draft = new TopicMapDraft(GraphQL, OperatorKey);

    [Test]
    public async Task TheWizard_BuildsTheMap_AndTheDraftKeepsAPointerToIt()
    {
        (await _draft.NotNull().CreateAsync())
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingFields));

        var building = await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
        building.Problem.Should().BeNull();
        building.State.Should().Be(nameof(TopicMapState.Building));

        var built = await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built));

        var context = built.Context;
        context["papers"].NotNull().GetValue<int>().Should().Be(CorpusFixture.Works.Count);
        context["topicPairs"].NotNull().GetValue<int>().Should().Be(32);
        context["coCitationTrack"].NotNull().GetValue<string>().Should().Be("Trusted");
        var mapId = context["mapId"].NotNull().GetValue<string>();
        mapId.Should().StartWith("map-");
        context.Should().NotContainKey("strongest", "the draft holds a summary, never the rows");

        // The pointer names the pairs the build wrote.
        await using (var db = await TopicMapDb())
            (await db.TopicPairs.CountAsync(p => p.RunId == mapId)).Should().Be(32);

        // Close the tab and come back: the draft is where it was left.
        var reloaded = await _draft.NotNull().LoadAsync();
        reloaded
            .Snapshot.NotNull()
            .ToJsonString()
            .Should()
            .Be(built.Snapshot.NotNull().ToJsonString());
    }

    [Test]
    public async Task TheServerMintsTheDraftId_AndADiscardedDraftIsGone()
    {
        var mine = Guid.NewGuid();
        var draft = new TopicMapDraft(GraphQL, OperatorKey, mine);

        (await draft.CreateAsync()).State.Should().Be(nameof(TopicMapState.ChoosingFields));
        draft
            .Id.Should()
            .NotBe(mine, "the start mints the id on the server; the client names none");

        // Start a new map: the page discards the draft it leaves.
        (await draft.DiscardAsync())
            .Should()
            .BeTrue();
        (await draft.LoadAsync()).Problem.Should().Be("NOT_FOUND");
    }

    [Test]
    public async Task StartingANewMapFromBuilt_ResetsTheDraft_AndLeavesNothingThatCountsTowardTheCap()
    {
        await _draft.NotNull().CreateAsync();
        await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
        await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built));
        (await Row()).DueAt.Should().NotBeNull("Built holds the map for review under a timer");
        (await _draft.NotNull().DiscardAsync())
            .Should()
            .BeFalse("a draft a build's outcome put in Built is the record of that build");

        // Start a new map, as the page does once a build began: the same draft, back at the first step.
        var reset = await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.StartOver));

        reset.Problem.Should().BeNull();
        reset.State.Should().Be(nameof(TopicMapState.ChoosingFields));
        reset
            .Context.Select(kv => kv.Value)
            .Should()
            .AllSatisfy(v => v.Should().BeNull(), "nothing is chosen on the new map");
        var row = await Row();
        row.DueAt.Should()
            .BeNull("leaving Built cancels its review timer, so nothing keeps the draft alive");
        row.InvokeToken.Should().BeNull();

        // Nothing is left that the user cannot throw away: a fresh draft is discarded like any other.
        (await _draft.NotNull().DiscardAsync())
            .Should()
            .BeTrue();
        (await _draft.NotNull().LoadAsync()).Problem.Should().Be("NOT_FOUND");
    }

    [TestCase(TopicMapState.Building)]
    [TestCase(TopicMapState.Built)]
    [TestCase(TopicMapState.BuildFailed)]
    [TestCase(TopicMapState.BuildCancelled)]
    [TestCase(TopicMapState.Expired)]
    public async Task StartingANewMap_ResetsTheDraft_FromEveryStateThatOffersIt(TopicMapState from)
    {
        await _draft.NotNull().CreateAsync();
        switch (from)
        {
            case TopicMapState.Building:
                // Started at once, before the run ends: the draft is in Building, or Built if the run won.
                (await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025))
                    .State.Should()
                    .Be(nameof(TopicMapState.Building));
                break;
            case TopicMapState.BuildFailed:
                // One field over two years holds fewer than two papers, so the build fails.
                await _draft.NotNull().BuildAsync([CorpusFixture.Hydrology], 1990, 1991);
                await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.BuildFailed));
                break;
            default:
                await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
                await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built));
                break;
        }
        await using (var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var row = db.SnapshotDrafts.Where(d =>
                d.Id == _draft.NotNull().Id && d.Machine == TopicMapMachine.MachineId
            );
            if (from == TopicMapState.BuildCancelled)
                // An operator's cancel of the run is what puts a build here; the page cannot, so the row is moved
                // as the run's outcome would move it.
                await row.ExecuteUpdateAsync(x =>
                    x.SetProperty(d => d.State, nameof(TopicMapState.BuildCancelled))
                        .SetProperty(d => d.DueAt, (DateTimeOffset?)null)
                        .SetProperty(d => d.ServerRecord, valueExpression: true)
                );
            if (from == TopicMapState.Expired)
            {
                var minuteAgo = TimeProvider.System.GetUtcNow().AddMinutes(-1);
                await row.ExecuteUpdateAsync(x => x.SetProperty(d => d.DueAt, minuteAgo));
                await SharedRecoverySetup
                    .Factory.Services.GetRequiredService<InvokeOutcomeReconciler>()
                    .FireDueTimersOnce(CancellationToken.None);
                (await _draft.NotNull().LoadAsync())
                    .State.Should()
                    .Be(nameof(TopicMapState.Expired));
            }
        }

        var reset = await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.StartOver));

        reset.Problem.Should().BeNull($"StartOver is offered from {from}");
        reset.State.Should().Be(nameof(TopicMapState.ChoosingFields));
        reset
            .Context.Select(kv => kv.Value)
            .Should()
            .AllSatisfy(v => v.Should().BeNull(), "nothing is chosen on the new map");
        var after = await Row();
        after.DueAt.Should().BeNull();
        after.InvokeToken.Should().BeNull("a run the draft was waiting on is cancelled with it");
        (await _draft.NotNull().DiscardAsync())
            .Should()
            .BeTrue("the new map is the owner's own draft");
    }

    [Test]
    public async Task AnAutosaveIntoBuilt_IsRefused()
    {
        var forged = new JsonObject
        {
            ["fields"] = new JsonArray("Hydrology"),
            ["fromYear"] = 2016,
            ["toYear"] = 2025,
            ["mapId"] = "map-forged",
            ["papers"] = 28,
            ["topicPairs"] = 999,
            ["coCitationTrack"] = "Trusted",
        };

        var saved = await _draft
            .NotNull()
            .SaveAsync(TopicMapDraft.Snapshot(nameof(TopicMapState.Built), forged));

        saved
            .Problem.Should()
            .Be(
                TopicMapDraft.NotServed,
                "the host serves the topic map only through its typed operations, which have no save, and only "
                    + "the build's outcome puts a draft in Built"
            );
        (await _draft.NotNull().LoadAsync()).Snapshot.Should().BeNull("nothing was written");
    }

    [Test]
    public async Task WhileBuilding_AHandFiredOutcome_AndAnAutosave_AreRefused()
    {
        await _draft.NotNull().CreateAsync();
        await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);

        var forged = await _draft
            .NotNull()
            .AdvanceAsync(
                "Building.done",
                new
                {
                    runId = "map-forged",
                    papers = 28,
                    topicPairs = 999,
                    coCitationTrack = "Trusted",
                    strongest = Array.Empty<object>(),
                    hiddenTwins = Array.Empty<object>(),
                }
            );
        forged
            .Problem.Should()
            .Be(
                TopicMapDraft.NotServed,
                "an outcome has no client field: only the run's outcome applies it"
            );

        var current = await _draft.NotNull().LoadAsync();
        current.State.Should().Be(nameof(TopicMapState.Building));
        var moved = await _draft
            .NotNull()
            .SaveAsync(
                TopicMapDraft.Snapshot(nameof(TopicMapState.ChoosingRange), current.Context)
            );
        moved
            .Problem.Should()
            .Be(
                TopicMapDraft.NotServed,
                "the host serves no save, so a client cannot leave a state with a run but through its events"
            );

        // The real outcome still lands.
        var built = await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built));
        built.Context["topicPairs"].NotNull().GetValue<int>().Should().Be(32);
    }

    // The built map's review timer: Built is held for a review window, then the server moves it on. A timer
    // trigger is named after the state it leaves.
    private const string ReviewTimer = "Built.after";

    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public async Task AHandFiredTimer_IsRefused()
    {
        await _draft.NotNull().CreateAsync();
        await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var built = await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built));

        // A timer trigger is not an event: the typed surface has no field for it, so a client cannot even name it.
        (await _draft.NotNull().AdvanceAsync(ReviewTimer))
            .Problem.Should()
            .Be(TopicMapDraft.NotServed, "a timer has no client field (Trax.Docs/adr/0048)");

        // Nor can host code acting for the draft's owner fire it, whatever role it holds.
        string owner;
        var draftId = _draft.NotNull().Id;
        var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            owner = (
                await scope
                    .ServiceProvider.GetRequiredService<IDataContext>()
                    .SnapshotDrafts.AsNoTracking()
                    .SingleAsync(d => d.Id == draftId && d.Machine == TopicMapMachine.MachineId)
            ).UserKey.NotNull();
        var advanceScope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (advanceScope.ConfigureAwait(false))
            (
                await advanceScope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .Service(TopicMapMachine.MachineId)
                    .NotNull()
                    .Advance(
                        new Actor.Client(owner, Trax.Samples.Recovery.Auth.RecoveryRoles.Operator),
                        _draft.NotNull().Id,
                        ReviewTimer
                    )
            )
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>()
                .Which.Reason.Should()
                .Be(
                    Trax.StateMachine.RefusalCode.TimerBound,
                    "only the server fires a timer, when it is due (Trax.Docs/adr/0048)"
                );

        var after = await _draft.NotNull().LoadAsync();
        after.State.Should().Be(nameof(TopicMapState.Built));
        after
            .Snapshot.NotNull()
            .ToJsonString()
            .Should()
            .Be(built.Snapshot.NotNull().ToJsonString(), "a refused timer writes nothing");
    }

    [Test]
    public async Task CancellingTheBuild_GoesBackToTheRange_AndCancelsItsRun()
    {
        // The run waits at its co-citation question until the cancel is sent, so it is never in its last
        // junction when the instance leaves Building: one that is would complete instead of cancelling.
        Trax.Effect.Models.WorkQueue.WorkQueue run;
        using (Decider.Hold(nameof(SameTopic)))
        {
            await _draft.NotNull().CreateAsync();
            await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
            run = (await Runs()).Single();

            var back = await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.CancelBuild));

            back.State.Should().Be(nameof(TopicMapState.ChoosingRange));
        }

        // Still queued, the entry is marked cancelled; held at the question, the run reads its cancel
        // flag at its next junction and ends cancelled.
        (
            await Shared.Testing.Polling.WaitUntilAsync(
                () => RunCancelled(run.ExternalId),
                TopicMapDraft.Patience,
                TimeSpan.FromMilliseconds(200)
            )
        )
            .Should()
            .BeTrue("leaving Building cancels the run it queued");
        (await _draft.NotNull().LoadAsync())
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingRange), "the cancelled run's outcome lands nowhere");
    }

    [Test]
    public async Task AFailedBuild_IsRebuiltByEnteringBuildingAgain_WithANewRun()
    {
        await _draft.NotNull().CreateAsync();

        // One field over two years holds fewer than two papers, so the build fails.
        (await _draft.NotNull().BuildAsync([CorpusFixture.Hydrology], 1990, 1991))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.BuildFailed));
        var failedRun = (await Runs()).Single();

        // Rebuild enters Building again: a new run, under a new token.
        (await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.Rebuild)))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        var runs = await Runs();
        runs.Should().HaveCount(2);
        runs[1].ExternalId.Should().NotBe(failedRun.ExternalId);
        await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.BuildFailed));

        // Edit the choices and build again.
        (await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.Edit)))
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingRange));
        (await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.Back)))
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingFields));
        (await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025))
            .Problem.Should()
            .BeNull();
        var built = await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built));
        built.Context["topicPairs"].NotNull().GetValue<int>().Should().Be(32);
    }

    [Test]
    public async Task AnOperator_CannotCancelTheBuild_ThroughTheRunOrItsEntry_OneByOneOrInBulk()
    {
        await _draft.NotNull().CreateAsync();
        await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var entry = (await Runs()).Single();

        var refused = new List<(bool Success, string? Message)>
        {
            await OperationAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mutation {{ operations {{ workQueue {{ cancelWorkQueueEntry(id: {entry.Id}) {{ success message }} }} }} }}"
                ),
                "workQueue",
                "cancelWorkQueueEntry"
            ),
        };

        // Once dispatched, the run itself: alone, and in a batch with an id that names nothing.
        long? run = null;
        (
            await Shared.Testing.Polling.WaitUntilAsync(
                async () =>
                    (run = (await EntryAsync(entry.Id).ConfigureAwait(false)).MetadataId)
                        is not null,
                TopicMapDraft.Patience,
                TimeSpan.FromMilliseconds(100)
            )
        )
            .Should()
            .BeTrue("the build's entry is dispatched");
        refused.Add(
            await OperationAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"mutation {{ operations {{ cancelExecution(id: {run}) {{ success message }} }} }}"
                ),
                "cancelExecution"
            )
        );
        var bulk = await OperationAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"mutation {{ operations {{ cancelExecutions(ids: [{run}, 999999999]) {{ success count message }} }} }}"
            ),
            "cancelExecutions"
        );

        refused
            .Should()
            .AllSatisfy(r =>
            {
                r.Success.Should().BeFalse("a user's draft is read-only to operators");
                r.Message.Should()
                    .Be(
                        Trax.Scheduler
                            .Services
                            .Operations
                            .OperationsService
                            .UserOwnedRunCancelRefusal
                    );
            });
        bulk.Success.Should().BeTrue();
        bulk.Message.Should()
            .EndWith(
                "1 skipped: "
                    + Trax.Scheduler.Services.Operations.OperationsService.UserOwnedRunCancelRefusal
            );

        // The build goes on, and its outcome moves the draft.
        (await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built)))
            .Context["topicPairs"]
            .NotNull()
            .GetValue<int>()
            .Should()
            .Be(32);
    }

    private async Task<(bool Success, string? Message)> OperationAsync(
        string mutation,
        params string[] path
    )
    {
        var response = await GraphQL.SendAsync(mutation, OperatorKey).ConfigureAwait(false);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var payload = response.GetData(["operations", .. path]);
        return (
            payload.GetProperty("success").GetBoolean(),
            payload.GetProperty("message").GetString()
        );
    }

    private static async Task<Trax.Effect.Models.WorkQueue.WorkQueue> EntryAsync(long id)
    {
        var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .WorkQueues.AsNoTracking()
                .SingleAsync(w => w.Id == id)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ARebuiltMap_LeavesTheOldMapToTheSweep_AndKeepsTheOneTheDraftPointsAt()
    {
        var sweeper = SharedRecoverySetup.Factory.Services.GetRequiredService<TopicMapSweeper>();
        await _draft.NotNull().CreateAsync();
        await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var first = (await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built)))
            .Context["mapId"]
            .NotNull()
            .GetValue<string>();

        (await _draft.NotNull().AdvanceAsync(nameof(TopicMapTrigger.Rebuild)))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        var rebuilt = await Shared.Testing.Polling.WaitUntilAsync(
            async () =>
                (await _draft.NotNull().LoadAsync().ConfigureAwait(false))
                    .Context["mapId"]
                    ?.GetValue<string>()
                    is { } id
                && !string.Equals(id, first, StringComparison.Ordinal),
            TopicMapDraft.Patience,
            TimeSpan.FromMilliseconds(200)
        );
        rebuilt.Should().BeTrue("the rebuild's outcome moves the draft's pointer to its new map");
        var second = (await _draft.NotNull().LoadAsync())
            .Context["mapId"]
            .NotNull()
            .GetValue<string>();

        // A map one sweep finds unreferenced is kept until the next, a build's pairs being written
        // a moment before its outcome points the draft at them.
        await sweeper.SweepAsync(CancellationToken.None);
        await sweeper.SweepAsync(CancellationToken.None);

        await using var db = await TopicMapDb();
        (await db.TopicPairs.CountAsync(p => p.RunId == first))
            .Should()
            .Be(0, "no draft points at the map the rebuild replaced");
        (await db.TopicPairs.CountAsync(p => p.RunId == second))
            .Should()
            .Be(32, "the draft points at the map the rebuild wrote");
    }

    [Test]
    public async Task TheSweep_NeverDeletesAMapAStoredDraftPointsAt_NorOneItSawOnlyOnce()
    {
        var sweeper = SharedRecoverySetup.Factory.Services.GetRequiredService<TopicMapSweeper>();
        await _draft.NotNull().CreateAsync();
        await _draft.NotNull().BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var kept = (await _draft.NotNull().WaitForStateAsync(nameof(TopicMapState.Built)))
            .Context["mapId"]
            .NotNull()
            .GetValue<string>();

        // Pairs no draft points at, written after the last sweep: the next sweep only notes them.
        const string Orphan = "map-orphan-sweep-test";
        await using (var db = await TopicMapDb())
        {
            db.TopicPairs.Add(
                new TopicPair
                {
                    RunId = Orphan,
                    WorkA = "a",
                    WorkB = "b",
                }
            );
            await db.SaveChangesAsync();
        }

        (await sweeper.SweepAsync(CancellationToken.None)).Should().NotContain(Orphan);
        (await sweeper.SweepAsync(CancellationToken.None)).Should().Contain(Orphan);

        await using var after = await TopicMapDb();
        (await after.TopicPairs.CountAsync(p => p.RunId == Orphan)).Should().Be(0);
        (await after.TopicPairs.CountAsync(p => p.RunId == kept)).Should().Be(32);
    }

    [Test]
    public async Task AnotherUser_CannotLoadTheDraft()
    {
        await _draft.NotNull().CreateAsync();
        var viewer = new GraphQLClient(Http);

        var response = await viewer.SendAsync(
            $$"""{ machines { topicMap { get(id: "{{_draft.NotNull().Id}}") { id state } } } }""",
            Auth.DemoKeys.Viewer
        );

        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        response
            .GetData("machines", "topicMap", "get")
            .ValueKind.Should()
            .Be(
                System.Text.Json.JsonValueKind.Null,
                "a draft belongs to the caller who started it; another user's id reads as an unknown one"
            );
    }

    private async Task<Trax.Effect.Models.SnapshotDraft.SnapshotDraft> Row()
    {
        var draftId = _draft.NotNull().Id;
        var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .SingleAsync(d => d.Id == draftId && d.Machine == TopicMapMachine.MachineId)
                .ConfigureAwait(false);
        }
    }

    private async Task<List<Trax.Effect.Models.WorkQueue.WorkQueue>> Runs()
    {
        var draftId = _draft.NotNull().Id;
        var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            return await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .WorkQueues.AsNoTracking()
                .Where(w => w.InvokingInstanceId == draftId)
                .OrderBy(w => w.Id)
                .ToListAsync()
                .ConfigureAwait(false);
        }
    }

    private static async Task<bool> RunCancelled(string externalId)
    {
        var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var entry = await db
                .WorkQueues.AsNoTracking()
                .SingleAsync(w => w.ExternalId == externalId)
                .ConfigureAwait(false);
            if (entry.Status == WorkQueueStatus.Cancelled)
                return true;
            return entry.MetadataId is { } id
                && await db
                    .Metadatas.AsNoTracking()
                    .AnyAsync(m => m.Id == id && m.TrainState == TrainState.Cancelled)
                    .ConfigureAwait(false);
        }
    }

    private static Task<TopicMapDbContext> TopicMapDb() =>
        SharedRecoverySetup
            .Factory.Services.GetRequiredService<IDbContextFactory<TopicMapDbContext>>()
            .CreateDbContextAsync();
}
