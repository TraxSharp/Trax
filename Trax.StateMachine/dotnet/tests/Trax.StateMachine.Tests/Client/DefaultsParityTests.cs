using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Client;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// <c>conformance/client/defaults.json</c> and <c>conformance/limits.json</c> hold every client default and shared limit
/// once. These hold the C# clients' and engine's own to them, as the TypeScript and Rust tests hold theirs. A value that
/// differs is fixed in the library, not here.
/// </summary>
public class DefaultsParityTests
{
    private static readonly JsonObject Defaults = Read("client", "defaults.json");
    private static readonly JsonObject Limits = Read("limits.json");

    private static JsonObject Read(params string[] parts) =>
        (JsonObject)
            JsonNode.Parse(
                File.ReadAllText(Path.Combine([FixturePaths.ConformanceRoot, .. parts]))
            )!;

    private static TimeSpan Ms(JsonNode? node) =>
        TimeSpan.FromMilliseconds(node!.GetValue<double>());

    private static TimeSpan[] MsList(string key) => Defaults[key]!.AsArray().Select(Ms).ToArray();

    private static long Bytes(JsonNode? node) => node!.GetValue<long>();

    // Built without any of the options, so each default is the one in force.
    private static GraphQLMachineServerOptions Unset() =>
        new()
        {
            Http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }),
            Endpoint = new Uri("https://h/graphql"),
            Machine = new ClientManifestMachine
            {
                Id = "turnstile",
                IrHash = "0",
                Ir = new JsonObject(),
            },
            Socket = new NoSocket(),
        };

    private sealed class NoSocket : ISubscriptionSocket
    {
        public IDisposable Subscribe(JsonObject payload, ISubscriptionSink sink) =>
            throw new NotSupportedException();
    }

    [Test]
    public void The_request_timeout_is_the_kits()
    {
        GraphQLMachineServer.DefaultRequestTimeout.Should().Be(Ms(Defaults["requestTimeoutMs"]));
        Unset().RequestTimeout.Should().Be(Ms(Defaults["requestTimeoutMs"]));
    }

    [Test]
    public void The_retry_and_resubscribe_delays_are_the_kits()
    {
        ServerMachineController.DefaultRetryDelays.Should().Equal(MsList("retryDelaysMs"));
        GraphQLMachineServer.DefaultResubscribeDelays.Should().Equal(MsList("resubscribeDelaysMs"));
        TraxClient.DefaultResubscribeDelays.Should().Equal(MsList("resubscribeDelaysMs"));
    }

    // A draw of 0 waits the whole step, and none waits less than the least fraction of it.
    [Test]
    public void A_resubscribe_wait_is_jittered_between_the_kits_bounds()
    {
        var jitter = Defaults["resubscribeJitter"]!;
        var step = TimeSpan.FromSeconds(1);
        Backoff
            .Delay([step], 0, () => 0.0)
            .Should()
            .Be(step * jitter["mostFraction"]!.GetValue<double>());
        Backoff
            .Delay([step], 0, () => 1.0)
            .Should()
            .Be(step * jitter["leastFraction"]!.GetValue<double>());
    }

    [Test]
    public void The_response_and_socket_message_sizes_are_the_kits()
    {
        var response = Bytes(Defaults["maxResponseBytes"]);
        TraxHttpTransportOptions.DefaultMaxResponseSize.Should().Be(response);
        new TraxHttpTransportOptions
        {
            Http = new HttpClient(),
            Endpoint = new Uri("https://h/graphql"),
        }
            .MaxResponseSize.Should()
            .Be(response);
        Unset().MaxResponseSize.Should().Be(response);
        new GraphQLSubscriptionSocketOptions { Endpoint = new Uri("wss://h/graphql") }
            .MaxMessageSize.Should()
            .Be((int)Bytes(Defaults["maxSocketMessageBytes"]!["dotnet"]));
    }

    [Test]
    public void The_sockets_timing_is_the_kits()
    {
        var timing = Defaults["socket"]!["dotnet"]!;
        var socket = new GraphQLSubscriptionSocketOptions { Endpoint = new Uri("wss://h/graphql") };
        socket.ConnectTimeout.Should().Be(Ms(timing["connectTimeoutMs"]));
        socket.KeepAliveInterval.Should().Be(Ms(timing["keepAliveIntervalMs"]));
        socket.KeepAliveTimeout.Should().Be(Ms(timing["keepAliveTimeoutMs"]));
    }

    [Test]
    public void Only_the_kits_refused_close_codes_turn_the_caller_away()
    {
        var codes = Defaults["closeCodes"]!;
        foreach (var refused in codes["refused"]!.AsObject())
            new SocketClosedException("closed", int.Parse(refused.Key))
                .Refused.Should()
                .BeTrue(refused.Key);
        foreach (var transient in codes["transient"]!.AsArray())
            new SocketClosedException("closed", transient!.GetValue<int>())
                .Refused.Should()
                .BeFalse(transient.ToString());
    }

    [Test]
    public void The_shared_limits_are_the_kits()
    {
        EventPlanner.MaxNesting.Should().Be(Limits["maxValueDepth"]!.GetValue<int>());
        EventPlanner.MaxExpansions.Should().Be(Limits["maxPlanExpansions"]!.GetValue<int>());
        TraxHttpTransport.MaxTimeout.Should().Be(Ms(Limits["maxTimerMs"]));
    }
}
