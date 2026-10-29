using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What a <see cref="ServerMachineController"/> sends, and when a send settles: only a declared event goes out, and
/// every send settles, whatever the server answered before it.
/// </summary>
public class ClientSendTests
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

    [TestCase("Discard")]
    [TestCase("Start")]
    [TestCase("Get")]
    [TestCase("Changed")]
    [TestCase("Nope")]
    public async Task A_trigger_the_machine_does_not_declare_is_refused_and_never_sent(
        string trigger
    )
    {
        var server = new HandServer();
        using var controller = Open(server, MachinePredictor.FromIr(Turnstile));
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));

        var outcome = await controller.SendAsync(trigger).WaitAsync(Wait);

        outcome
            .Should()
            .BeOfType<SendOutcome.Refused>()
            .Which.Problem.Code.Should()
            .Be("NO_TRANSITION");
        controller.View.Status.Should().Be(MachineStatus.Ready);
        // The next event goes out, so nothing the refused one could have sent is still waiting.
        var push = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait))
            .Trigger.Should()
            .Be("Coin");
        server.Events.Reader.TryRead(out _).Should().BeFalse();
        push.IsCompleted.Should().BeFalse();
    }

    [Test]
    public async Task Outcomes_and_timers_are_refused_with_the_engines_code_and_never_sent()
    {
        var machine = new ClientManifestMachine
        {
            Id = "turnstile",
            IrHash = "unchecked",
            Ir = Declaring(
                outcomes: new JsonObject { ["Locked.done"] = new JsonObject() },
                timers: [new JsonObject { ["state"] = "Unlocked", ["trigger"] = "Unlocked.after" }]
            ),
        };
        var server = new HandServer();
        using var controller = new ServerMachineController(
            new ServerMachineOptions
            {
                Machine = machine,
                Server = server,
                Id = "i-1",
            }
        );
        await server.Answer(controller, ServerAnswer.Of(At(1, "Locked")));

        var outcome = await controller.SendAsync("Locked.done").WaitAsync(Wait);
        var timer = await controller.SendAsync("Unlocked.after").WaitAsync(Wait);

        outcome
            .Should()
            .BeOfType<SendOutcome.Refused>()
            .Which.Problem.Code.Should()
            .Be("OUTCOME_BOUND");
        timer
            .Should()
            .BeOfType<SendOutcome.Refused>()
            .Which.Problem.Code.Should()
            .Be("TIMER_BOUND");
        server.Events.Reader.TryRead(out _).Should().BeFalse();
    }

    [TestCase("Discard")]
    [TestCase("Get")]
    [TestCase("Start")]
    [TestCase("Changed")]
    [TestCase("Nope")]
    public async Task The_server_never_sends_an_event_as_one_of_the_machines_operations(
        string trigger
    )
    {
        // Discard is declared here, as a host would never serve it: only its field's name keeps it from going out.
        var ir = (JsonObject)Turnstile.Ir.DeepClone();
        ir["triggers"]!.AsArray().Add("Discard");
        var host = new CountingHost();
        var server = new GraphQLMachineServer(
            new GraphQLMachineServerOptions
            {
                Http = new HttpClient(host),
                Endpoint = new Uri("https://host/trax/graphql"),
                Machine = Turnstile with
                {
                    Ir = ir,
                    Documents =
                    [
                        .. new[] { "start", "get", "discard", "changed", "coin" }.Select(
                            operation => new ClientManifestDocument
                            {
                                Id = $"{operation}-1",
                                Operation = operation,
                                Document =
                                    $"query Q($id: UUID!, $requestId: String!) {{ {operation} }}",
                            }
                        ),
                    ],
                },
                Socket = new NoSocket(),
            }
        );

        var answer = await server.EventAsync("i-1", trigger, null, "r1", null).WaitAsync(Wait);

        answer.Problem!.Code.Should().Be("NO_TRANSITION");
        host.Requests.Should().Be(0);
    }

    private sealed class CountingHost : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":{"machines":{"turnstile":{}}}}"""),
                }
            );
        }
    }

    private sealed class NoSocket : Trax.Client.ISubscriptionSocket
    {
        public IDisposable Subscribe(JsonObject payload, Trax.Client.ISubscriptionSink sink) =>
            throw new NotSupportedException();
    }

    // The turnstile's IR with the outcomes and timers given added to it.
    private static JsonObject Declaring(JsonObject outcomes, JsonArray timers)
    {
        var ir = (JsonObject)Turnstile.Ir.DeepClone();
        ir["outcomes"] = outcomes;
        ir["timers"] = timers;
        return ir;
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
}
