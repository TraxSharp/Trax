using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Client;
using Trax.StateMachine.Tests.Fakes;

namespace Trax.StateMachine.Tests.Client;

/// <summary>
/// What the GraphQL server and socket accept as an endpoint, and what they read from one: a token travels only over
/// TLS unless the host is this machine or the caller says otherwise.
/// </summary>
public class TransportTests
{
    private static readonly ClientManifestMachine Turnstile = new()
    {
        Id = "turnstile",
        IrHash = MachinePredictor.For(DeclarativeTurnstile.Built).IrHash,
        Ir = (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built))!,
        Documents =
        [
            new ClientManifestDocument
            {
                Id = "get-1",
                Operation = "get",
                Document = "query Get($id: UUID!) { x }",
            },
            new ClientManifestDocument
            {
                Id = "changed-1",
                Operation = "changed",
                Document = "subscription Changed($id: UUID!) { x }",
            },
        ],
    };

    private sealed class NoSocket : ISubscriptionSocket
    {
        public IDisposable Subscribe(
            string documentId,
            JsonObject variables,
            ISubscriptionSink sink
        ) => throw new NotSupportedException();
    }

    private static GraphQLMachineServer Server(
        string endpoint,
        bool allowInsecure = false,
        HttpClient? http = null
    ) =>
        new(
            new GraphQLMachineServerOptions
            {
                Http = http ?? new HttpClient(),
                Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute),
                Machine = Turnstile,
                Socket = new NoSocket(),
                AllowInsecureTransport = allowInsecure,
            }
        );

    private static GraphQLSubscriptionSocket Socket(string endpoint, bool allowInsecure = false) =>
        new(
            new GraphQLSubscriptionSocketOptions
            {
                Endpoint = new Uri(endpoint),
                AllowInsecureTransport = allowInsecure,
            }
        );

    [TestCase("http://api.example.com/trax/graphql")]
    [TestCase("http://10.0.0.5/trax/graphql")]
    public void A_cleartext_endpoint_on_another_host_is_refused(string endpoint)
    {
        var build = () => Server(endpoint);

        build.Should().Throw<ArgumentException>().WithMessage("*not over TLS*");
    }

    [TestCase("ws://api.example.com/trax/graphql")]
    [TestCase("ws://[2001:db8::1]/trax/graphql")]
    public void A_cleartext_socket_on_another_host_is_refused(string endpoint)
    {
        var build = () => Socket(endpoint);

        build.Should().Throw<ArgumentException>().WithMessage("*not over TLS*");
    }

    [Test]
    public void The_refusal_names_the_host_but_not_the_query()
    {
        var build = () => Server("http://api.example.com/graphql?access_token=SECRET");

        build.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("SECRET");
    }

    [TestCase("https://api.example.com/trax/graphql")]
    [TestCase("http://localhost:5000/trax/graphql")]
    [TestCase("http://127.0.0.1:5000/trax/graphql")]
    [TestCase("http://127.4.5.6:5000/trax/graphql")]
    [TestCase("http://[::1]:5000/trax/graphql")]
    public void Tls_or_a_loopback_host_is_accepted(string endpoint)
    {
        var build = () => Server(endpoint);

        build.Should().NotThrow();
    }

    [TestCase("wss://api.example.com/trax/graphql")]
    [TestCase("ws://localhost:5000/trax/graphql")]
    [TestCase("ws://127.0.0.1:5000/trax/graphql")]
    [TestCase("ws://[::1]:5000/trax/graphql")]
    public void A_socket_over_tls_or_to_a_loopback_host_is_accepted(string endpoint)
    {
        var build = () => Socket(endpoint);

        build.Should().NotThrow();
    }

    [Test]
    public void The_caller_may_accept_cleartext_explicitly()
    {
        var server = () => Server("http://api.example.com/trax/graphql", allowInsecure: true);
        var socket = () => Socket("ws://api.example.com/trax/graphql", allowInsecure: true);

        server.Should().NotThrow();
        socket.Should().NotThrow();
    }

    [Test]
    public void A_relative_endpoint_is_checked_against_the_clients_base_address()
    {
        var cleartext = () =>
            Server(
                "trax/graphql",
                http: new HttpClient { BaseAddress = new Uri("http://api.example.com/") }
            );
        var tls = () =>
            Server(
                "trax/graphql",
                http: new HttpClient { BaseAddress = new Uri("https://api.example.com/") }
            );

        cleartext.Should().Throw<ArgumentException>();
        tls.Should().NotThrow();
    }
}
