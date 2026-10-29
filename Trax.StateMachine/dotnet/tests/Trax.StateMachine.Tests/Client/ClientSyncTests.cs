using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// <see cref="ServerMachineController.SyncToAsync"/> over the kit's wizard and a server the test answers by hand, beyond
/// what the client kit's sync transcripts hold every client to: a sync waits for an earlier send, the step budget,
/// planning without a predictor, an invalid request and a disposed controller.
/// </summary>
public class ClientSyncTests
{
    // How long a test waits for the controller before it fails; every wait here is on a signal, never a duration.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly ClientManifestMachine Wizard = Manifest();

    private static readonly PlannedEvent Fill = new(
        "Update",
        new JsonObject { ["email"] = "ada@example.com", ["name"] = "Ada" }
    );

    private static readonly string[] Navigation = ["Begin", "Next", "Back"];

    private static ClientManifestMachine Manifest()
    {
        var ir = (JsonObject)
            JsonNode.Parse(
                File.ReadAllText(
                    Path.Combine(FixturePaths.ConformanceRoot, "plan", "wizard.ir.json")
                )
            )!;
        var manifest = new ClientManifestMachine
        {
            Id = "wizard",
            IrHash = "",
            Ir = ir,
        };
        return manifest with { IrHash = MachinePredictor.FromIr(manifest).IrHash };
    }

    private static ServerSnapshot At(long revision, string state, JsonObject? context = null) =>
        new()
        {
            Revision = revision,
            State = state,
            Context = context ?? [],
            DueAtKnown = true,
        };

    private static JsonObject Filled() => new() { ["email"] = "ada@example.com", ["name"] = "Ada" };

    [Test]
    public async Task A_sync_plans_once_an_earlier_send_has_settled_from_what_it_confirmed()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.FromIr(Wizard));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Start")));

        var begin = controller.SendAsync("Begin");
        var sync = controller.SyncToAsync("Review", Fill, Navigation);
        var first = await Next(server);
        first.Trigger.Should().Be("Begin");
        first.Answer.SetResult(ServerAnswer.Of(At(2, "Details")));
        (await begin.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();

        var update = await Next(server);
        update.Trigger.Should().Be("Update");
        update.Answer.SetResult(ServerAnswer.Of(At(3, "Details", Filled())));
        var next = await Next(server);
        next.Trigger.Should().Be("Next");
        next.Answer.SetResult(ServerAnswer.Of(At(4, "Review", Filled())));

        var outcome = await sync.WaitAsync(Wait);
        outcome.Should().BeOfType<SyncOutcome.Synced>().Which.Snapshot.Revision.Should().Be(4);
        controller.View.Unsaved.Should().BeFalse();
    }

    [Test]
    public async Task A_sync_that_never_gets_closer_stops_at_twice_the_machines_states()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.FromIr(Wizard));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Start")));

        var sync = controller.SyncToAsync("Review", Fill, Navigation);
        // The server takes every Begin and stays where it was, so the plan starts again each time.
        for (var revision = 2; revision < 10; revision++)
        {
            var sent = await Next(server);
            sent.Trigger.Should().Be("Begin");
            sent.Answer.SetResult(ServerAnswer.Of(At(revision, "Start")));
        }

        (await sync.WaitAsync(Wait)).Should().BeOfType<SyncOutcome.Exceeded>();
        server.Events.Reader.TryRead(out _).Should().BeFalse();
        controller.View.Unsaved.Should().BeTrue();
    }

    [Test]
    public async Task Without_a_predictor_a_sync_plans_from_the_manifests_IR_and_sends_no_prediction()
    {
        var server = new HandServer();
        using var controller = Open(server, predictor: null);
        await server.Answer(controller, ServerAnswer.Of(At(1, "Details")));

        var sync = controller.SyncToAsync("Review", Fill, Navigation);
        var update = await Next(server);
        update.Trigger.Should().Be("Update");
        update.ClientResult.Should().BeNull();
        update.Answer.SetResult(ServerAnswer.Of(At(2, "Details", Filled())));
        var next = await Next(server);
        next.Trigger.Should().Be("Next");
        next.Answer.SetResult(ServerAnswer.Of(At(3, "Review", Filled())));

        (await sync.WaitAsync(Wait)).Should().BeOfType<SyncOutcome.Synced>();
    }

    [Test]
    public async Task A_sync_the_planner_cannot_take_sends_nothing_and_stays_unsaved()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.FromIr(Wizard));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Review", Filled())));

        var outcome = await controller.SyncToAsync("Sent", null, ["Next", "Send"]).WaitAsync(Wait);

        outcome.Should().Be(new SyncOutcome.Invalid("Send", null));
        server.Events.Reader.TryRead(out _).Should().BeFalse();
        controller.View.Unsaved.Should().BeTrue();
    }

    [Test]
    public async Task A_sync_waiting_for_its_turn_settles_when_the_controller_is_disposed()
    {
        var server = new HandServer();
        var controller = Open(server, MachinePredictor.FromIr(Wizard));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Start")));

        var sync = controller.SyncToAsync("Review", Fill, Navigation);
        (await Next(server)).Trigger.Should().Be("Begin");
        controller.Dispose();

        (await sync.WaitAsync(Wait))
            .Should()
            .BeOfType<SyncOutcome.Failed>()
            .Which.Problem.Code.Should()
            .Be(ClientProblemCodes.Disposed);
    }

    private static async Task<HandServer.Sent> Next(HandServer server) =>
        await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

    private static ServerMachineController Open(HandServer server, IMachinePredictor? predictor) =>
        new(
            new ServerMachineOptions
            {
                Machine = Wizard,
                Server = server,
                Predictor = predictor,
                Id = "i-1",
            }
        );
}
