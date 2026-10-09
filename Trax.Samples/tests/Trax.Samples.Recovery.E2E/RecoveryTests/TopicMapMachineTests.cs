using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The <c>topic-map</c> wizard, a user's own machine, driven over GraphQL as the page drives it: choose
/// the fields, choose the years, build. <c>Building</c> runs the topic map train, and only that run's
/// outcome moves the draft on; a client cannot put the draft in <c>Built</c> itself or fire the
/// outcome by hand.
/// </summary>
[TestFixture]
public class TopicMapMachineTests : RecoveryTestFixture
{
    private TopicMapDraft _draft = null!;

    [SetUp]
    public void NewDraft() => _draft = new TopicMapDraft(GraphQL, OperatorKey);

    [Test]
    public async Task TheWizard_BuildsTheMap_AndTheDraftKeepsAPointerToIt()
    {
        (await _draft.CreateAsync()).State.Should().Be(nameof(TopicMapState.ChoosingFields));

        var building = await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025);
        building.Problem.Should().BeNull();
        building.State.Should().Be(nameof(TopicMapState.Building));

        var built = await _draft.WaitForStateAsync(nameof(TopicMapState.Built));

        var context = built.Context;
        context["papers"]!.GetValue<int>().Should().Be(CorpusFixture.Works.Count);
        context["topicPairs"]!.GetValue<int>().Should().Be(32);
        context["coCitationTrack"]!.GetValue<string>().Should().Be("Trusted");
        var mapId = context["mapId"]!.GetValue<string>();
        mapId.Should().StartWith("map-");
        context.Should().NotContainKey("strongest", "the draft holds a summary, never the rows");

        // The pointer names the pairs the build wrote.
        await using (var db = await TopicMapDb())
            (await db.TopicPairs.CountAsync(p => p.RunId == mapId)).Should().Be(32);

        // Close the tab and come back: the draft is where it was left.
        var reloaded = await _draft.LoadAsync();
        reloaded.Snapshot!.ToJsonString().Should().Be(built.Snapshot!.ToJsonString());
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

        var saved = await _draft.SaveAsync(
            TopicMapDraft.Snapshot(nameof(TopicMapState.Built), forged)
        );

        saved.Problem.Should().Be("state-reserved", "only the build's outcome puts a draft there");
        (await _draft.LoadAsync()).Snapshot.Should().BeNull("nothing was written");
    }

    [Test]
    public async Task WhileBuilding_AHandFiredOutcome_AndAnAutosave_AreRefused()
    {
        await _draft.CreateAsync();
        await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025);

        var forged = await _draft.AdvanceAsync(
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
        forged.Problem.Should().Be("outcome-bound", "only the run's outcome applies it");

        var current = await _draft.LoadAsync();
        current.State.Should().Be(nameof(TopicMapState.Building));
        var moved = await _draft.SaveAsync(
            TopicMapDraft.Snapshot(nameof(TopicMapState.ChoosingRange), current.Context)
        );
        moved.Problem.Should().Be("draft-invoking", "an autosave cannot leave a state with a run");

        // The real outcome still lands.
        var built = await _draft.WaitForStateAsync(nameof(TopicMapState.Built));
        built.Context["topicPairs"]!.GetValue<int>().Should().Be(32);
    }

    [Test]
    public async Task CancellingTheBuild_GoesBackToTheRange_AndCancelsItsRun()
    {
        await _draft.CreateAsync();
        await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var run = (await Runs()).Single();

        var back = await _draft.AdvanceAsync(nameof(TopicMapTrigger.CancelBuild));

        back.State.Should().Be(nameof(TopicMapState.ChoosingRange));

        // Still queued, the entry is marked cancelled; dispatched, the run reads its cancel flag at
        // its next junction and ends cancelled.
        (
            await Shared.Testing.Polling.WaitUntilAsync(
                () => RunCancelled(run.ExternalId),
                TopicMapDraft.Patience,
                TimeSpan.FromMilliseconds(200)
            )
        )
            .Should()
            .BeTrue("leaving Building cancels the run it queued");
        (await _draft.LoadAsync())
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingRange), "the cancelled run's outcome lands nowhere");
    }

    [Test]
    public async Task AFailedBuild_IsRebuiltByEnteringBuildingAgain_WithANewRun()
    {
        await _draft.CreateAsync();

        // One field over two years holds fewer than two papers, so the build fails.
        (await _draft.BuildAsync([CorpusFixture.Hydrology], 1990, 1991))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        await _draft.WaitForStateAsync(nameof(TopicMapState.BuildFailed));
        var failedRun = (await Runs()).Single();

        // Rebuild enters Building again: a new run, under a new token.
        (await _draft.AdvanceAsync(nameof(TopicMapTrigger.Rebuild)))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        var runs = await Runs();
        runs.Should().HaveCount(2);
        runs[1].ExternalId.Should().NotBe(failedRun.ExternalId);
        await _draft.WaitForStateAsync(nameof(TopicMapState.BuildFailed));

        // Edit the choices and build again.
        (await _draft.AdvanceAsync(nameof(TopicMapTrigger.Edit)))
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingRange));
        (await _draft.AdvanceAsync(nameof(TopicMapTrigger.Back)))
            .State.Should()
            .Be(nameof(TopicMapState.ChoosingFields));
        (await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025)).Problem.Should().BeNull();
        var built = await _draft.WaitForStateAsync(nameof(TopicMapState.Built));
        built.Context["topicPairs"]!.GetValue<int>().Should().Be(32);
    }

    [Test]
    public async Task AnOperator_CannotCancelTheBuild_ThroughTheRunOrItsEntry_OneByOneOrInBulk()
    {
        await _draft.CreateAsync();
        await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var entry = (await Runs()).Single();

        var refused = new List<(bool Success, string? Message)>
        {
            await OperationAsync(
                $"mutation {{ operations {{ workQueue {{ cancelWorkQueueEntry(id: {entry.Id}) {{ success message }} }} }} }}",
                "workQueue",
                "cancelWorkQueueEntry"
            ),
        };

        // Once dispatched, the run itself: alone, and in a batch with an id that names nothing.
        long? run = null;
        (
            await Shared.Testing.Polling.WaitUntilAsync(
                async () => (run = (await EntryAsync(entry.Id)).MetadataId) is not null,
                TopicMapDraft.Patience,
                TimeSpan.FromMilliseconds(100)
            )
        )
            .Should()
            .BeTrue("the build's entry is dispatched");
        refused.Add(
            await OperationAsync(
                $"mutation {{ operations {{ cancelExecution(id: {run}) {{ success message }} }} }}",
                "cancelExecution"
            )
        );
        var bulk = await OperationAsync(
            $"mutation {{ operations {{ cancelExecutions(ids: [{run}, 999999999]) {{ success count message }} }} }}",
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
        (
            await _draft.WaitForStateAsync(nameof(TopicMapState.Built))
        ).Context["topicPairs"]!.GetValue<int>().Should().Be(32);
    }

    private async Task<(bool Success, string? Message)> OperationAsync(
        string mutation,
        params string[] path
    )
    {
        var response = await GraphQL.SendAsync(mutation, OperatorKey);
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var payload = response.GetData(["operations", .. path]);
        return (
            payload.GetProperty("success").GetBoolean(),
            payload.GetProperty("message").GetString()
        );
    }

    private static async Task<Trax.Effect.Models.WorkQueue.WorkQueue> EntryAsync(long id)
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == id);
    }

    [Test]
    public async Task ARebuiltMap_LeavesTheOldMapToTheSweep_AndKeepsTheOneTheDraftPointsAt()
    {
        var sweeper = SharedRecoverySetup.Factory.Services.GetRequiredService<TopicMapSweeper>();
        await _draft.CreateAsync();
        await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var first = (await _draft.WaitForStateAsync(nameof(TopicMapState.Built))).Context[
            "mapId"
        ]!.GetValue<string>();

        (await _draft.AdvanceAsync(nameof(TopicMapTrigger.Rebuild)))
            .State.Should()
            .Be(nameof(TopicMapState.Building));
        var rebuilt = await Shared.Testing.Polling.WaitUntilAsync(
            async () =>
                (await _draft.LoadAsync()).Context["mapId"]?.GetValue<string>() is { } id
                && id != first,
            TopicMapDraft.Patience,
            TimeSpan.FromMilliseconds(200)
        );
        rebuilt.Should().BeTrue("the rebuild's outcome moves the draft's pointer to its new map");
        var second = (await _draft.LoadAsync()).Context["mapId"]!.GetValue<string>();

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
        await _draft.CreateAsync();
        await _draft.BuildAsync(CorpusFixture.Fields, 2016, 2025);
        var kept = (await _draft.WaitForStateAsync(nameof(TopicMapState.Built))).Context[
            "mapId"
        ]!.GetValue<string>();

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
        await _draft.CreateAsync();
        var viewer = new GraphQLClient(Http);

        var response = await viewer.SendAsync(
            $$"""
            mutation { dispatch { stateMachine { loadSnapshot(input: {
              machine: "topic-map", id: "{{_draft.Id}}" }) { output { snapshot problem { code } } } } } }
            """,
            Auth.DemoKeys.Viewer
        );

        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        response
            .GetData("dispatch", "stateMachine", "loadSnapshot", "output", "snapshot")
            .ValueKind.Should()
            .Be(System.Text.Json.JsonValueKind.Null, "a draft belongs to the caller who saved it");
    }

    private async Task<List<Trax.Effect.Models.WorkQueue.WorkQueue>> Runs()
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .Where(w => w.InvokingInstanceId == _draft.Id)
            .OrderBy(w => w.Id)
            .ToListAsync();
    }

    private static async Task<bool> RunCancelled(string externalId)
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var entry = await db.WorkQueues.AsNoTracking().SingleAsync(w => w.ExternalId == externalId);
        if (entry.Status == WorkQueueStatus.Cancelled)
            return true;
        return entry.MetadataId is { } id
            && await db
                .Metadatas.AsNoTracking()
                .AnyAsync(m => m.Id == id && m.TrainState == TrainState.Cancelled);
    }

    private static Task<TopicMapDbContext> TopicMapDb() =>
        SharedRecoverySetup
            .Factory.Services.GetRequiredService<IDbContextFactory<TopicMapDbContext>>()
            .CreateDbContextAsync();
}
