using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.Utils;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// An invoked run is a train run like any other, so the features a train already has work on it: a decider is
/// asked, and its answer recorded on the run; a short circuit is the output its machine reads; its junctions write
/// their events and progress; and a remote worker runs it from the request a scheduler sends. Each is a row cell of
/// the interaction matrix's machine-features table (<c>Trax.Core/docs/interaction-matrix.md</c>). See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class InvokedRunFeatureTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    // Every wait in this class: long past any step here, short enough that a hang fails the test.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private readonly InvokedLaneDecider _decider = new();

    private InvokeCluster _cluster = null!;

    // Registers the machines: their hook and their reconciler. No scheduler.
    private ClusterHost _api = null!;

    // Dispatches and runs trains, records decisions and asks the decider; registers no machines, so every
    // outcome comes back through the sweep.
    private ClusterHost _worker = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _api = _cluster.Host(machines: true, scheduler: false);
        _worker = _cluster.Host(
            machines: false,
            scheduler: true,
            configure: s => s.AddSingleton<IDecider>(_decider),
            data: d => d.AddDecisionRecording()
        );
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _worker.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.Reset();
    }

    [TearDown]
    public void ReleaseGate() => InvokedStepGate.Release();

    [Test]
    public async Task A_reentry_asks_the_decider_afresh_and_never_replays_the_failed_runs_recorded_answer()
    {
        _decider.Script(InvokedLane.Express, InvokedLane.Ground);
        var user = NewUser();
        var id = Guid.NewGuid();
        await Enter(user, id, FeatureTrigger.Decide, StepMachine.Context("ok", note: "lanes"));

        // The decider sends the first run down the express lane, which fails.
        var firstRun = await _worker.DispatchAndRun((await Row(id)).InvokeToken!);
        (await _worker.Run(firstRun)).TrainState.Should().Be(TrainState.Failed);
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Failed" });

        // Entering the state again queues a new run, which is not a requeue of the first.
        (await Advance(user, id, FeatureTrigger.Retry))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var secondRun = await _worker.DispatchAndRun((await Row(id)).InvokeToken!);
        var second = await _worker.Run(secondRun);
        second.TrainState.Should().Be(TrainState.Completed);
        second.ReplayDecisionsOf.Should().BeNull("an invoked run is never a replay of another");
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done" });

        _decider
            .Asked.Should()
            .Be(
                2,
                $"each entry's run asks its own question; the failed run's answer is not reused. See {Adr}"
            );
        var first = (await Decisions(firstRun)).Should().ContainSingle().Subject;
        first.Answer.Should().Contain(nameof(InvokedLane.Express));
        first.Replayed.Should().BeFalse();
        var again = (await Decisions(secondRun)).Should().ContainSingle().Subject;
        again.Answer.Should().Contain(nameof(InvokedLane.Ground));
        again.Replayed.Should().BeFalse("the decider was asked afresh");
        Context(await Row(id))["artifact"]!.GetValue<string>().Should().Be("lane:ground:lanes");
    }

    [Test]
    public async Task A_short_circuit_in_an_invoked_train_is_the_output_its_OnDone_reads()
    {
        var shortened = NewUser();
        var shortId = Guid.NewGuid();
        await Enter(
            shortened,
            shortId,
            FeatureTrigger.Shorten,
            StepMachine.Context(InvokedStepModes.Short, note: "cut")
        );
        await _worker.DispatchAndRun((await Row(shortId)).InvokeToken!);

        // A short circuit that fails is ignored, and the run ends at its last junction.
        var through = NewUser();
        var throughId = Guid.NewGuid();
        await Enter(
            through,
            throughId,
            FeatureTrigger.Shorten,
            StepMachine.Context(InvokedStepModes.Ok, note: "whole")
        );
        await _worker.DispatchAndRun((await Row(throughId)).InvokeToken!);

        (await _api.Sweep())
            .Should()
            .HaveCount(2)
            .And.AllSatisfy(d => d.Should().BeEquivalentTo(new { To = "Done" }));
        Context(await Row(shortId))["artifact"]!
            .GetValue<string>()
            .Should()
            .Be(
                "short:cut",
                $"the short circuit's value is the run's output, and the output OnDone reduces. See {Adr}"
            );
        Context(await Row(throughId))["artifact"]!.GetValue<string>().Should().Be("artifact:whole");
    }

    [Test]
    public async Task An_invoked_run_writes_its_junction_events_and_progress_as_any_run_does()
    {
        var observed = _cluster.Host(
            machines: false,
            scheduler: true,
            data: d => d.AddJunctionEvents()
        );
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Gate));
        var token = (await _api.Row(instance.Id, SystemStepMachine.MachineId))!.InvokeToken!;
        var runId = await observed.Dispatch(token);
        var job = observed.RunJob(runId);
        await InvokedStepGate.Entered.WaitAsync(Bound);

        (await observed.Run(runId))
            .CurrentlyRunningJunction.Should()
            .Be(
                nameof(InvokedStepEnter),
                $"the progress provider reports the junction the invoked run is in. See {Adr}"
            );

        InvokedStepGate.Release();
        await job.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(Bound);
        // Disposing the host drains the junction-event writer.
        await observed.DisposeAsync();

        var run = await _api.Run(runId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.CurrentlyRunningJunction.Should().BeNull("every junction has ended");
        List<JunctionRun> steps;
        using (var scope = _api.Services.CreateScope())
            steps = await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .JunctionRuns.AsNoTracking()
                .ForRun(runId)
                .ToListAsync();
        steps
            .Select(s => (s.NodeId, s.State))
            .Should()
            .Equal(
                [
                    ("InvokedStepEnter#0", JunctionRunState.Completed),
                    ("InvokedStepFinish#0", JunctionRunState.Completed),
                ],
                "an invoked run's junctions write their events with their node ids"
            );
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done" });
    }

    [Test]
    public async Task An_invoked_run_sent_to_a_remote_worker_runs_there_and_its_outcome_comes_back()
    {
        await using var remote = _cluster.Host(
            machines: false,
            scheduler: false,
            configure: s => s.AddTraxJobRunner(o => o.AllowUnsignedRequests())
        );
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok, note: "remote"));
        var token = (await _api.Row(instance.Id, SystemStepMachine.MachineId))!.InvokeToken!;
        var runId = await _worker.Dispatch(token);

        // The request the HTTP submitter sends, across the wire.
        var input = _worker.HeldInput(runId)!;
        var sent = new RemoteJobRequest(
            runId,
            JsonSerializer.Serialize(
                input,
                input.GetType(),
                TraxJsonSerializationOptions.ManifestProperties
            ),
            input.GetType().FullName
        );
        var received = JsonSerializer.Deserialize<RemoteJobRequest>(
            JsonSerializer.Serialize(sent)
        )!;
        using (var scope = remote.Services.CreateScope())
            await scope
                .ServiceProvider.GetRequiredService<ITraxRequestHandler>()
                .ExecuteJobAsync(received)
                .WaitAsync(Bound);

        (await _api.Run(runId)).TrainState.Should().Be(TrainState.Completed);
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new { To = "Done", Applied = "done" },
                $"the worker recorded the output its machine reads, in the run's own row. See {Adr}"
            );
        Context((await _api.Row(instance.Id, SystemStepMachine.MachineId))!)["artifact"]!
            .GetValue<string>()
            .Should()
            .Be("artifact:remote");
    }

    private async Task Enter(string user, Guid id, FeatureTrigger trigger, JsonObject context)
    {
        using var scope = _api.Services.CreateScope();
        var drafts = Drafts(scope);
        (
            await drafts.Autosave(
                user,
                id,
                StepMachine.Json(InvokedFeatureMachine.MachineId, "Idle", context)
            )
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        (await drafts.Advance(user, id, trigger.ToString()))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
    }

    private async Task<AdvanceOutcome> Advance(string user, Guid id, FeatureTrigger trigger)
    {
        using var scope = _api.Services.CreateScope();
        return await Drafts(scope).Advance(user, id, trigger.ToString());
    }

    private static ISnapshotDraftService Drafts(IServiceScope scope) =>
        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service(InvokedFeatureMachine.MachineId)!;

    private async Task<SnapshotDraft> Row(Guid id) =>
        (await _api.Row(id, InvokedFeatureMachine.MachineId))!;

    private async Task<List<Effect.Models.RecordedDecision.RecordedDecision>> Decisions(long runId)
    {
        using var scope = _api.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == runId)
            .ToListAsync();
    }

    private static JsonObject Context(SnapshotDraft row) => JsonNode.Parse(row.Context)!.AsObject();

    private static string NewUser() => "user-" + Guid.NewGuid().ToString("N");
}
