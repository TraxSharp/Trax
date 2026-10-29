using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What a <see cref="ServerMachineController"/> shows while an event is pending and once the server answers, over a
/// server whose every answer the test gives by hand. The server applies every transition: a prediction is shown only
/// until the answer replaces it.
/// </summary>
public class ClientPredictionTests
{
    // How long a test waits for the controller before it fails; every wait here is on a signal, never a duration.
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly ClientManifestMachine Turnstile = new()
    {
        Id = "turnstile",
        IrHash = MachinePredictor.For(DeclarativeTurnstile.Built).IrHash,
        Ir = (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built))!,
    };

    private static ServerSnapshot At(long? revision, string state, JsonObject? context = null) =>
        new()
        {
            Revision = revision,
            State = state,
            Context = context ?? [],
            DueAtKnown = true,
        };

    [Test]
    public async Task A_prediction_the_server_answers_differently_becomes_the_servers_snapshot()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.For(DeclarativeTurnstile.Built));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));

        var send = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.ClientResult.Should()
            .Be(
                "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}"
            );
        await Until(controller, v => v.Pending);
        controller.View.Snapshot!.Context["paidWith"]!.GetValue<string>().Should().Be("quarter");

        sent.Answer.SetResult(
            ServerAnswer.Of(At(2, "Unlocked", new JsonObject { ["paidWith"] = "dollar" }))
        );
        var outcome = await send.WaitAsync(Wait);

        outcome.Should().BeOfType<SendOutcome.Confirmed>();
        controller.View.Pending.Should().BeFalse();
        controller.View.Snapshot!.Context["paidWith"]!.GetValue<string>().Should().Be("dollar");
        controller.View.Confirmed.Should().Be(controller.View.Snapshot);
    }

    [Test]
    public async Task A_predictor_built_from_the_IR_predicts_the_bytes_the_compiled_machine_does()
    {
        var fromIr = new HandServer();
        var compiled = new HandServer();
        using var irController = Open(fromIr, MachinePredictor.FromIr(Turnstile));
        using var compiledController = Open(
            compiled,
            MachinePredictor.For(DeclarativeTurnstile.Built)
        );
        await fromIr.Answer(irController, ServerAnswer.Of(At(1, "Locked")));
        await compiled.Answer(compiledController, ServerAnswer.Of(At(1, "Locked")));

        _ = irController.SendAsync("Coin", new JsonObject { ["coin"] = "dollar" });
        _ = compiledController.SendAsync("Coin", new JsonObject { ["coin"] = "dollar" });
        var fromIrSent = await fromIr.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        var compiledSent = await compiled.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        fromIrSent.ClientResult.Should().NotBeNull().And.Be(compiledSent.ClientResult);
    }

    [Test]
    public async Task Without_a_predictor_an_event_is_sent_with_no_prediction_and_nothing_is_shown_until_it_answers()
    {
        var server = new HandServer();
        using var controller = Open(server, predictor: null);
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));

        var send = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.ClientResult.Should().BeNull();
        await Until(controller, v => v.Pending);
        controller.View.Snapshot!.State.Should().Be("Locked");
        controller.CanSend("Coin").Should().BeTrue();
        controller.AvailableTriggers().Should().Equal("Coin");

        sent.Answer.SetResult(ServerAnswer.Of(At(2, "Unlocked")));
        (await send.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
        controller.View.Snapshot!.State.Should().Be("Unlocked");
    }

    [Test]
    public async Task An_answer_without_a_revision_is_taken_and_the_next_one_is_taken_after_it()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.FromIr(Turnstile));
        await server.Answer(
            controller,
            ServerAnswer.Of(At(5, "Unlocked", new JsonObject { ["paidWith"] = "dollar" }))
        );

        var push = controller.SendAsync("Push");
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Answer.SetResult(
            ServerAnswer.Of(At(null, "Locked"))
        );
        (await push.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
        controller.View.Revision.Should().BeNull();
        controller.View.Confirmed!.State.Should().Be("Locked");

        var coin = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Answer.SetResult(
            ServerAnswer.Of(At(2, "Unlocked", new JsonObject { ["paidWith"] = "quarter" }))
        );
        (await coin.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();

        controller.View.Revision.Should().Be(2);
        controller.View.Confirmed!.State.Should().Be("Unlocked");
    }

    [Test]
    public async Task A_failed_request_is_a_value_and_never_an_exception()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.For(DeclarativeTurnstile.Built));
        await server.Answer(
            controller,
            ServerAnswer.Of(At(1, "Unlocked", new JsonObject { ["paidWith"] = "dollar" }))
        );

        var send = controller.SendAsync("Push");
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Answer.SetException(
            new HttpRequestException("connection refused")
        );

        var outcome = await send.WaitAsync(Wait);
        outcome
            .Should()
            .BeOfType<SendOutcome.Failed>()
            .Which.Problem.Should()
            .Be(
                new MachineProblem(
                    MachineProblemKind.Transport,
                    ClientProblemCodes.TransportError,
                    "connection refused"
                )
            );
        controller.View.Snapshot!.State.Should().Be("Unlocked");
    }

    [Test]
    public async Task A_change_handler_that_throws_on_the_confirming_change_leaves_the_send_settled_and_the_queue_draining()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.For(DeclarativeTurnstile.Built));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));
        var throwing = false;
        var heard = 0;
        controller.Changed += (_, _) =>
        {
            if (Volatile.Read(ref throwing))
                throw new InvalidOperationException("handler bug");
        };
        controller.Changed += (_, _) => Interlocked.Increment(ref heard);

        var coin = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        var push = controller.SendAsync("Push");
        var first = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        Volatile.Write(ref throwing, true);
        var before = Volatile.Read(ref heard);
        first.Answer.SetResult(
            ServerAnswer.Of(At(2, "Unlocked", new JsonObject { ["paidWith"] = "quarter" }))
        );

        (await coin.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
        var second = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        second.Trigger.Should().Be("Push");
        second.Answer.SetResult(ServerAnswer.Of(At(3, "Locked")));
        (await push.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
        Volatile.Read(ref heard).Should().BeGreaterThan(before);
    }

    [Test]
    public async Task A_change_handler_that_throws_when_an_event_is_queued_does_not_fail_the_send()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.For(DeclarativeTurnstile.Built));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));
        controller.Changed += (_, _) => throw new InvalidOperationException("handler bug");

        var coin = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Answer.SetResult(
            ServerAnswer.Of(At(2, "Unlocked", new JsonObject { ["paidWith"] = "quarter" }))
        );

        (await coin.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
        controller.View.Confirmed!.State.Should().Be("Unlocked");
    }

    [Test]
    public async Task After_a_divergence_the_next_queued_event_waits_for_the_read_and_is_predicted_from_it()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.For(DeclarativeTurnstile.Built));
        // What the client holds is stale: the server's instance was unlocked with a dollar since.
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));

        var coin = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        var push = controller.SendAsync("Push");
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Answer.SetResult(
            ServerAnswer.Refused("CLIENT_DIVERGENCE", "The prediction differs.")
        );
        (await coin.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Refused>();
        (await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Of(At(5, "Unlocked", new JsonObject { ["paidWith"] = "dollar" }))
        );

        var second = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        second.Trigger.Should().Be("Push");
        second
            .ClientResult.Should()
            .Be("{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}");
        second.Answer.SetResult(ServerAnswer.Of(At(6, "Locked")));
        (await push.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
    }

    [Test]
    public async Task A_new_incarnation_pushed_while_a_read_is_pending_is_read_and_held()
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.For(DeclarativeTurnstile.Built));
        await server.Answer(controller, ServerAnswer.Of(At(10, "Locked")));
        server.Push(new StreamMessage.Changed(At(11, "Locked") with { RowId = "row-a" }));
        await Until(controller, v => v.RowId == "row-a");

        // The instance is deleted and created again while a reload's read is pending. A read's answer carries no
        // incarnation, so the pending one cannot say which it read.
        server.Push(new StreamMessage.Reload());
        var first = await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        var recreated = At(1, "Unlocked", new JsonObject { ["paidWith"] = "dollar" });
        server.Push(new StreamMessage.Changed(recreated with { RowId = "row-b" }));
        first.SetResult(ServerAnswer.Of(recreated));
        (await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Of(recreated)
        );

        await Until(controller, v => v.RowId == "row-b");
        controller.View.Revision.Should().Be(1);
        controller.View.Confirmed!.State.Should().Be("Unlocked");
    }

    [Test]
    public void A_predictor_of_another_version_than_the_manifest_is_refused()
    {
        var other = Turnstile with { IrHash = new string('0', 64) };

        var build = () =>
            new ServerMachineController(
                new ServerMachineOptions
                {
                    Machine = other,
                    Server = new HandServer(),
                    Predictor = MachinePredictor.For(DeclarativeTurnstile.Built),
                }
            );

        build.Should().Throw<ArgumentException>().WithMessage("*another version*");
    }

    // A review whose reviewer check, edit count and closing rule only the server can evaluate: custom rules the IR names
    // and a predictor built from it holds no handler for.
    private static readonly ClientManifestMachine Review = WithHash(
        new ClientManifestMachine
        {
            Id = "review",
            IrHash = "",
            Ir = (JsonObject)
                JsonNode.Parse(
                    """
                    {"committedStates":[],"context":{"Approved":{"fields":[]},"Closed":{"fields":[]},
                     "Draft":{"fields":[{"constraints":[],"name":"edits","nullable":false,"type":"integer"}]},
                     "Escalated":{"fields":[]},"Held":{"fields":[]},"Rejected":{"fields":[]}},
                     "id":"review","initialContext":{"edits":0},"initialState":"Draft","inputs":{},
                     "invariants":{"Closed":{"name":"review:closable","rule":"custom"}},
                     "states":["Approved","Closed","Draft","Escalated","Held","Rejected"],
                     "transitions":[
                      {"from":"Draft","reduce":{"name":"review:count-edit","reduce":"custom"},"to":"Draft","trigger":"Edit"},
                      {"from":"Draft","guard":{"name":"review:is-reviewer","rule":"custom"},"reduce":{"reduce":"clear"},"to":"Approved","trigger":"Approve"},
                      {"from":"Draft","reduce":{"reduce":"clear"},"to":"Escalated","trigger":"Approve"},
                      {"from":"Draft","guard":{"name":"review:is-reviewer","rule":"custom"},"reduce":{"reduce":"clear"},"to":"Rejected","trigger":"Reject"},
                      {"from":"Draft","reduce":{"reduce":"clear"},"to":"Closed","trigger":"Close"},
                      {"from":"Draft","reduce":{"reduce":"clear"},"to":"Held","trigger":"Hold"}],
                     "triggers":["Approve","Close","Edit","Hold","Reject"],"version":1}
                    """
                )!,
        }
    );

    private static ClientManifestMachine WithHash(ClientManifestMachine machine) =>
        machine with
        {
            IrHash = MachinePredictor.FromIr(machine).IrHash,
        };

    private static readonly JsonObject Drafted = new() { ["edits"] = 0 };

    [Test]
    public async Task IrMachine_UnhandledCustomReduction_IsNotPredicted()
    {
        var server = new HandServer();
        using var controller = OpenReview(server);
        await server.Answer(
            controller,
            ServerAnswer.Of(At(1, "Draft", Drafted.DeepClone().AsObject()))
        );

        var send = controller.SendAsync("Edit");
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.ClientResult.Should().BeNull();
        sent.Answer.SetResult(ServerAnswer.Of(At(2, "Draft", new JsonObject { ["edits"] = 1 })));
        (await send.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
        controller.View.Snapshot!.Context["edits"]!.GetValue<int>().Should().Be(1);
    }

    [Test]
    public async Task IrMachine_UnhandledCustomGuardBesideAnotherEdge_IsNotPredicted()
    {
        var server = new HandServer();
        using var controller = OpenReview(server);
        await server.Answer(
            controller,
            ServerAnswer.Of(At(1, "Draft", Drafted.DeepClone().AsObject()))
        );

        controller.CanSend("Approve").Should().BeTrue();
        _ = controller.SendAsync("Approve");
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.ClientResult.Should().BeNull();
        await Until(controller, v => v.Pending);
        controller.View.Snapshot!.State.Should().Be("Draft");
    }

    [Test]
    public async Task IrMachine_RefusalFromUnhandledCustomGuard_IsSent_AndNothingAfterItIsPredicted()
    {
        var server = new HandServer();
        using var controller = OpenReview(server);
        await server.Answer(
            controller,
            ServerAnswer.Of(At(1, "Draft", Drafted.DeepClone().AsObject()))
        );

        controller.CanSend("Reject").Should().BeTrue();
        _ = controller.SendAsync("Reject");
        _ = controller.SendAsync("Hold");
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.Trigger.Should().Be("Reject");
        sent.ClientResult.Should().BeNull();
        await Until(controller, v => v.Pending);
        controller.View.Snapshot!.State.Should().Be("Draft");
    }

    [Test]
    public async Task IrMachine_TransitionIntoAStateWithAnUnhandledCustomRequirement_IsNotPredicted()
    {
        var server = new HandServer();
        using var controller = OpenReview(server);
        await server.Answer(
            controller,
            ServerAnswer.Of(At(1, "Draft", Drafted.DeepClone().AsObject()))
        );

        _ = controller.SendAsync("Close");
        _ = controller.SendAsync("Hold");
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.ClientResult.Should().BeNull();
        await Until(controller, v => v.Pending);
        controller.View.Snapshot!.State.Should().Be("Draft");
    }

    private static ServerMachineController OpenReview(HandServer server) =>
        new(
            new ServerMachineOptions
            {
                Machine = Review,
                Server = server,
                Predictor = MachinePredictor.FromIr(Review),
                Id = "i-1",
            }
        );

    private static ServerMachineController Open(HandServer server, IMachinePredictor? predictor) =>
        new(
            new ServerMachineOptions
            {
                Machine = Turnstile,
                Server = server,
                Predictor = predictor,
                Id = "i-1",
            }
        );

    // Waits until the view holds what `ready` says, on the controller's own signal.
    private static async Task Until(
        ServerMachineController controller,
        Func<MachineView, bool> ready
    )
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (ready(controller.View))
                reached.TrySetResult();
        }
        controller.Changed += Check;
        try
        {
            Check(null, EventArgs.Empty);
            await reached.Task.WaitAsync(Wait);
        }
        finally
        {
            controller.Changed -= Check;
        }
    }
}
