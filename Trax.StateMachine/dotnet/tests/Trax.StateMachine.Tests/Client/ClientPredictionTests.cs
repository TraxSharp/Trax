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

    private static ServerSnapshot At(long revision, string state, JsonObject? context = null) =>
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
