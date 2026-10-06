using System.Text;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Audit;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.Audit;

/// <summary>
/// The audit pipeline "captures each request". A request the endpoint gate refuses is a request,
/// and the one an operator most needs a record of. The endpoint policy refuses inside execution,
/// never from the HTTP interceptor, so the refusal reaches the audit listener.
/// <para>Enforces <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c>, and
/// <c>docs/adr/0035-a-refused-request-is-always-audited.md</c>: a subscription refused when it
/// subscribes is audited although accepted ones are skipped by default.</para>
/// </summary>
[Property("adr", "docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md")]
[Property("adr", "docs/adr/0035-a-refused-request-is-always-audited.md")]
[TestFixture]
public class AuditRefusedRequestTests
{
    private const string Adr = "docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md";

    private const string ApiKey = "audit-refusal-key";

    [Test]
    public async Task ARequestTheEndpointGateRefuses_IsAudited()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();
        CapturingSink.Entries.Clear();

        // Control: an authorized request is audited.
        using (var authorized = Post("{ auditPing }"))
        {
            authorized.Headers.Add("X-Api-Key", ApiKey);
            (await client.SendAsync(authorized)).EnsureSuccessStatusCode();
        }
        (await WaitForEntriesAsync(1)).Should().BeTrue("the control request is audited");
        CapturingSink.Entries.Single().Document.Should().Contain("auditPing");

        // No credential: the endpoint gate refuses it.
        var refused = await client.SendAsync(Post("{ auditPing }"));
        (await refused.Content.ReadAsStringAsync()).Should().Contain("TRAX_AUTHORIZATION");

        (await WaitForEntriesAsync(2))
            .Should()
            .BeTrue($"a request the endpoint gate refused must leave an audit record ({Adr})");
        var refusal = CapturingSink.Entries.Last();
        refusal.Success.Should().BeFalse();
        refusal.ErrorText.Should().Be("TRAX_AUTHORIZATION");
        refusal.PrincipalId.Should().Be("<anonymous>");
        refusal.Document.Should().Contain("auditPing");
    }

    [Test]
    public async Task ASubscriptionTheOperationsGateRefuses_IsAudited_UnderTheDefaultOptions()
    {
        using var host = await StartAsync(gateOperationsToRole: "Admin");
        var client = host.GetTestClient();
        CapturingSink.Entries.Clear();

        // Signed in, without the role the operations gate asks for: the data-change feed refuses
        // the subscriber when it subscribes.
        using var request = Post("subscription { onDataChanged { domain } }");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Accept.ParseAdd("text/event-stream");
        var response = await client.SendAsync(request);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TRAX_AUTHORIZATION");

        (await WaitForEntriesAsync(1))
            .Should()
            .BeTrue(
                "a refused subscription is audited even though accepted ones are skipped by default "
                    + "(docs/adr/0035-a-refused-request-is-always-audited.md)"
            );
        var refusal = CapturingSink.Entries.Single();
        refusal.Success.Should().BeFalse();
        refusal.ErrorText.Should().Be("TRAX_AUTHORIZATION");
        refusal.PrincipalId.Should().EndWith("auditor");
        refusal.Document.Should().Contain("onDataChanged");
    }

    [TestCase("not-a-registered-key", SocketRefusalReason.Rejected)]
    [TestCase(null, SocketRefusalReason.Missing)]
    public async Task ASocketRefusedAtConnectionInit_IsAudited_WithoutItsCredential(
        string? key,
        SocketRefusalReason reason
    )
    {
        using var host = await StartAsync();
        CapturingSink.Entries.Clear();

        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var ws = await client.ConnectClosingAsync(
            new Uri("ws://localhost/trax/graphql"),
            default
        );
        object payload = key is null ? new { } : new { apiKey = key };
        await ws.SendAsync(
            Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(new { type = "connection_init", payload })
            ),
            System.Net.WebSockets.WebSocketMessageType.Text,
            true,
            default
        );

        (await WaitForEntriesAsync(1))
            .Should()
            .BeTrue(
                "a connection refused at the handshake never reaches the executor, and must "
                    + "still leave an audit record (docs/adr/0035-a-refused-request-is-always-audited.md)"
            );
        var refusal = CapturingSink.Entries.Single();
        refusal.Success.Should().BeFalse();
        refusal.PrincipalId.Should().Be("<anonymous>");
        refusal.Document.Should().BeEmpty();
        refusal.ErrorText.Should().StartWith("TRAX_SOCKET_REFUSED at connection_init");
        refusal.Metadata!["stage"].Should().Be("connection_init");
        // The host's one scheme judges every connection, a credential or none.
        refusal.Metadata["scheme"].Should().Be("ApiKey");
        refusal
            .Metadata["reason"]
            .Should()
            .Be(
                reason == SocketRefusalReason.Missing ? "missing credential" : "credential rejected"
            );
        if (key is not null)
            System
                .Text.Json.JsonSerializer.Serialize(refusal)
                .Should()
                .NotContain(key, "the credential is never recorded");
    }

    public enum SocketRefusalReason
    {
        Missing,
        Rejected,
    }

    [Test]
    public async Task AnErrorInALaterEventOfAnAcceptedSubscription_IsAuditedOnce()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();
        CapturingSink.Entries.Clear();

        using var request = Post("subscription Ticks { auditTick }");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Accept.ParseAdd("text/event-stream");
        var body = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        body.Should().Contain("\"ok\"", "the first event is delivered");

        (await WaitForEntriesAsync(1))
            .Should()
            .BeTrue("an event that fails after the subscription was accepted is audited");
        // allowed-delay: gives a duplicate entry the writer's flush interval to appear.
        await Task.Delay(100);
        var entry = CapturingSink.Entries.Should().ContainSingle().Which;
        entry.Success.Should().BeFalse();
        entry.PrincipalId.Should().EndWith("auditor");
        entry.OperationName.Should().Be("Ticks");
        entry.Document.Should().Contain("auditTick");
        entry.ErrorText.Should().EndWith("at auditTick").And.NotContain("secret-detail");
        entry.Metadata!["subscriptionEvent"].Should().Be("error");
    }

    [Test]
    public async Task AnEventWhoseAliasedFieldsAllFail_IsAuditedAsOneEntry()
    {
        using var host = await StartAsync();
        var client = host.GetTestClient();
        CapturingSink.Entries.Clear();

        // Two events, each selecting the same failing field under 200 aliases.
        var aliases = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"f{i}: bad"));
        using var request = Post($"subscription Probes {{ auditProbe {{ {aliases} }} }}");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Accept.ParseAdd("text/event-stream");
        var body = await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        body.Should().Contain("f199", "both events are delivered with their errors");

        (await WaitForEntriesAsync(2)).Should().BeTrue("each failing event is audited");
        // allowed-delay: gives an entry per failing field the writer's flush interval to appear.
        await Task.Delay(100);
        CapturingSink
            .Entries.Should()
            .HaveCount(2, "a failing event leaves one entry, however many of its fields fail");
        foreach (var entry in CapturingSink.Entries)
        {
            entry.Success.Should().BeFalse();
            entry.OperationName.Should().Be("Probes");
            entry.Metadata!["subscriptionEvent"].Should().Be("error");
            entry.Metadata["subscriptionEventErrors"].Should().Be("200");
            entry.ErrorText.Should().Contain(" at ").And.NotContain("secret-detail");
            entry
                .ErrorText!.Length.Should()
                .BeLessThanOrEqualTo(
                    new TraxAuditOptions().MaxErrorTextLength + "...[truncated]".Length
                );
        }
    }

    private static async Task<bool> WaitForEntriesAsync(int count)
    {
        // The writer drains the channel on a 10 ms flush interval; two seconds is generous.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (CapturingSink.Entries.Count >= count)
                return true;
            await Task.Delay(10); // determinism: poll interval of a condition wait, bounded above
        }
        return CapturingSink.Entries.Count >= count;
    }

    private static HttpRequestMessage Post(string query) =>
        new(HttpMethod.Post, "/trax/graphql")
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { query }),
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static Task<IHost> StartAsync(string? gateOperationsToRole = null) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddTraxApiKeyAuth(keys => keys.Add(ApiKey, id: "auditor"));
                        s.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        s.AddSingleton(discovery);
                        s.AddSingleton(Substitute.For<IEffectRegistry>());
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());
                        s.AddTraxGraphQL(g =>
                        {
                            g.ExposeOperationQueries()
                                .RequireAuthorization()
                                .AddTypeExtension<AuditPingQuery>()
                                .AddTypeExtension<AuditTickSubscription>()
                                .AddTypeExtension<AuditProbeSubscription>()
                                .AddAudit<CapturingSink>(o =>
                                    o.FlushInterval = TimeSpan.FromMilliseconds(10)
                                );
                            return gateOperationsToRole is null
                                ? g
                                : g.GateOperations(roles: gateOperationsToRole);
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

    [ExtendObjectType("RootQuery")]
    public class AuditPingQuery
    {
        [TraxAuthorize]
        public string AuditPing() => "pong";
    }

    [ExtendObjectType("LifecycleSubscriptions")]
    public class AuditTickSubscription
    {
        [TraxAuthorize]
        [Subscribe(With = nameof(Ticks))]
        public string AuditTick([EventMessage] string tick) =>
            tick == "boom" ? throw new InvalidOperationException("secret-detail") : tick;

        public async IAsyncEnumerable<string> Ticks()
        {
            yield return "ok";
            await Task.Yield();
            yield return "boom";
        }
    }

    [ExtendObjectType("LifecycleSubscriptions")]
    public class AuditProbeSubscription
    {
        [TraxAuthorize]
        [Subscribe(With = nameof(Probes))]
        public AuditProbe AuditProbe([EventMessage] string probe) => new();

        public async IAsyncEnumerable<string> Probes()
        {
            yield return "one";
            await Task.Yield();
            yield return "two";
        }
    }

    public class AuditProbe
    {
        public string? Bad() => throw new InvalidOperationException("secret-detail");
    }

    private sealed class CapturingSink : ITraxAuditSink
    {
        public static readonly System.Collections.Concurrent.ConcurrentQueue<TraxAuditEntry> Entries =
            new();

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            foreach (var entry in batch)
                Entries.Enqueue(entry);
            return Task.CompletedTask;
        }
    }
}
