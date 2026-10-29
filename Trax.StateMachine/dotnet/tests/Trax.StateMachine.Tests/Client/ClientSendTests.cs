using System.Text.Json.Nodes;
using System.Threading.Channels;
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

    [Test]
    public async Task Start_settles_once_a_read_that_failed_has_been_read_again()
    {
        var server = new StepServer();
        using var controller = Open(
            server,
            id: "i-1",
            retryDelays: [TimeSpan.FromMilliseconds(100)]
        );

        var start = controller.StartAsync();
        (await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetException(
            new HttpRequestException("dropped")
        );
        var again = await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        start.IsCompleted.Should().BeFalse("the read is still being retried");
        again.SetResult(ServerAnswer.Of(At(1, "Locked")));
        await start.WaitAsync(Wait);
        controller.View.Status.Should().Be(MachineStatus.Ready);
    }

    [Test]
    public async Task Start_settles_once_a_start_answered_SERVER_OUTDATED_has_been_retried()
    {
        var server = new StepServer();
        using var controller = Open(
            server,
            id: null,
            retryDelays: [TimeSpan.FromMilliseconds(100)]
        );

        var start = controller.StartAsync();
        (await server.Starts.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Refused("SERVER_OUTDATED", "Another version answered.")
        );
        var again = await server.Starts.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        start.IsCompleted.Should().BeFalse("the start is still being retried");
        again.SetResult(ServerAnswer.Of(At(1, "Locked"), "i-1"));
        await start.WaitAsync(Wait);
        controller.View.Status.Should().Be(MachineStatus.Ready);
    }

    [Test]
    public async Task A_send_after_a_refused_start_fails_NOT_READY()
    {
        var server = new StepServer();
        using var controller = Open(server, id: null, retryDelays: []);
        var start = controller.StartAsync();
        // Sent while the start is in flight, it waits for it, and settles once the start is refused.
        var early = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        (await server.Starts.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Refused("FORBIDDEN", "Not yours.")
        );
        await start.WaitAsync(Wait);

        var late = await controller.SendAsync("Push").WaitAsync(Wait);

        late.Should().BeOfType<SendOutcome.Failed>().Which.Problem.Code.Should().Be("NOT_READY");
        (await early.WaitAsync(Wait))
            .Should()
            .BeOfType<SendOutcome.Failed>()
            .Which.Problem.Code.Should()
            .Be("NOT_READY");
        server.Events.Reader.TryRead(out _).Should().BeFalse();
    }

    [TestCase("FORBIDDEN")]
    [TestCase("TRANSPORT")]
    public async Task A_send_after_a_first_read_that_gave_up_fails_NOT_READY(string answer)
    {
        var server = new StepServer();
        using var controller = Open(server, id: "i-1", retryDelays: []);
        var start = controller.StartAsync();
        var read = await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        if (answer == "TRANSPORT")
            read.SetException(new HttpRequestException("dropped"));
        else
            read.SetResult(ServerAnswer.Refused(answer, "No."));
        await start.WaitAsync(Wait);

        var send = await controller.SendAsync("Push").WaitAsync(Wait);

        send.Should().BeOfType<SendOutcome.Failed>().Which.Problem.Code.Should().Be("NOT_READY");
        server.Events.Reader.TryRead(out _).Should().BeFalse();
    }

    [Test]
    public async Task An_answer_whose_numbers_cannot_be_held_fails_the_send_and_the_next_one_goes_out()
    {
        var server = new StepServer();
        using var controller = Open(
            server,
            id: "i-1",
            retryDelays: [],
            predictor: MachinePredictor.FromIr(Turnstile)
        );
        var start = controller.StartAsync();
        (await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Of(At(1, "Unlocked", new JsonObject { ["paidWith"] = "dollar" }))
        );
        await start.WaitAsync(Wait);

        var push = controller.SendAsync("Push");
        (await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait)).Answer.SetResult(
            ServerAnswer.Of(At(2, "Locked", (JsonObject)JsonNode.Parse("""{"n":1e400}""")!))
        );

        (await push.WaitAsync(Wait))
            .Should()
            .BeOfType<SendOutcome.Failed>()
            .Which.Problem.Code.Should()
            .Be("TRANSPORT_ERROR");
        var next = controller.SendAsync("Push");
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);
        sent.Trigger.Should().Be("Push");
        sent.Answer.SetResult(ServerAnswer.Of(At(2, "Locked")));
        (await next.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
    }

    [Test]
    public async Task A_first_read_whose_numbers_cannot_be_held_settles_the_start_as_a_failure()
    {
        var server = new StepServer();
        using var controller = Open(
            server,
            id: "i-1",
            retryDelays: [],
            predictor: MachinePredictor.FromIr(Turnstile)
        );

        var start = controller.StartAsync();
        (await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Of(At(1, "Locked", (JsonObject)JsonNode.Parse("""{"n":-1e400}""")!))
        );

        await start.WaitAsync(Wait);
        controller.View.Status.Should().Be(MachineStatus.Loading);
        controller.View.Problem!.Code.Should().Be("TRANSPORT_ERROR");
    }

    [Test]
    public async Task A_prediction_that_cannot_be_written_is_sent_without_one_and_the_queue_goes_on()
    {
        var server = new StepServer();
        using var controller = Open(
            server,
            id: "i-1",
            retryDelays: [],
            predictor: new UnwritablePredictions(MachinePredictor.FromIr(Turnstile))
        );
        var start = controller.StartAsync();
        (await server.Reads.Reader.ReadAsync().AsTask().WaitAsync(Wait)).SetResult(
            ServerAnswer.Of(At(1, "Locked"))
        );
        await start.WaitAsync(Wait);

        var coin = controller.SendAsync("Coin", new JsonObject { ["coin"] = "quarter" });
        var sent = await server.Events.Reader.ReadAsync().AsTask().WaitAsync(Wait);

        sent.ClientResult.Should().BeNull();
        sent.Answer.SetResult(ServerAnswer.Of(At(2, "Locked")));
        (await coin.WaitAsync(Wait)).Should().BeOfType<SendOutcome.Confirmed>();
    }

    // A predictor whose every prediction out of Locked fails to serialize, as one holding a number past a double's range
    // does.
    private sealed class UnwritablePredictions(IMachinePredictor inner) : IMachinePredictor
    {
        public string IrHash => inner.IrHash;

        public AdvanceResult Advance(Snapshot snapshot, string trigger, JsonNode? input) =>
            inner.Advance(snapshot, trigger, input);

        public RehydrationResult Rehydrate(string json) => inner.Rehydrate(json);

        public string Serialize(Snapshot snapshot) =>
            snapshot.State == "Unlocked"
                ? throw new ArgumentException(
                    "NaN and Infinity are not valid canonical JSON numbers."
                )
                : inner.Serialize(snapshot);

        public bool CanFire(Snapshot snapshot, string trigger, JsonNode? input) =>
            inner.CanFire(snapshot, trigger, input);

        public IReadOnlyList<string> AvailableTriggers(Snapshot snapshot) =>
            inner.AvailableTriggers(snapshot);

        public PlanResult Plan(
            Snapshot snapshot,
            string target,
            PlannedEvent? fields,
            IReadOnlyList<string> navigation,
            int? maxDepth = null
        ) => inner.Plan(snapshot, target, fields, navigation, maxDepth);
    }

    // A server whose every start, event and read the test answers by hand, as each arrives.
    private sealed class StepServer : IMachineServer
    {
        public Channel<TaskCompletionSource<ServerAnswer>> Starts { get; } =
            Channel.CreateUnbounded<TaskCompletionSource<ServerAnswer>>();

        public Channel<TaskCompletionSource<ServerAnswer>> Reads { get; } =
            Channel.CreateUnbounded<TaskCompletionSource<ServerAnswer>>();

        public Channel<HandServer.Sent> Events { get; } =
            Channel.CreateUnbounded<HandServer.Sent>();

        private static Task<ServerAnswer> Hand(Channel<TaskCompletionSource<ServerAnswer>> to)
        {
            var answer = new TaskCompletionSource<ServerAnswer>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            to.Writer.TryWrite(answer);
            return answer.Task;
        }

        public Task<ServerAnswer> StartAsync(
            JsonNode? input,
            string requestId,
            CancellationToken cancellationToken = default
        ) => Hand(Starts);

        public Task<ServerAnswer> EventAsync(
            string id,
            string trigger,
            JsonNode? input,
            string requestId,
            string? clientResult,
            CancellationToken cancellationToken = default
        )
        {
            var sent = new HandServer.Sent(trigger, input, clientResult);
            Events.Writer.TryWrite(sent);
            return sent.Answer.Task;
        }

        public Task<ServerAnswer> GetAsync(
            string id,
            CancellationToken cancellationToken = default
        ) => Hand(Reads);

        public IDisposable Subscribe(string id, Action<StreamMessage> onMessage) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static ServerMachineController Open(
        StepServer server,
        string? id,
        IReadOnlyList<TimeSpan> retryDelays,
        IMachinePredictor? predictor = null
    ) =>
        new(
            new ServerMachineOptions
            {
                Machine = Turnstile,
                Server = server,
                Predictor = predictor,
                Id = id,
                RetryDelays = retryDelays,
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
}
