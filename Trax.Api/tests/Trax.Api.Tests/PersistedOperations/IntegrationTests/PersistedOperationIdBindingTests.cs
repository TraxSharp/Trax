using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HotChocolate.Language;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// A persisted-operation id names the document the store holds for it, and nothing else. A
/// document one caller sends alongside an id the store does not hold (never uploaded, or
/// deactivated) must not become what that id runs for the next caller who asks for it by id.
///
/// <para>Enforces <c>docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md")]
[TestFixture]
[Category("Integration")]
public class PersistedOperationIdBindingTests
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IPersistedOperationStore _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        await DatabaseMigrator.Migrate(PostgresFixture.ConnectionString);
        (_host, _client, _store) = await StartAsync(requirePersisted: false);
    }

    private static async Task<(IHost, HttpClient, IPersistedOperationStore)> StartAsync(
        bool requirePersisted
    )
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(s =>
                    {
                        s.AddLogging();
                        s.AddRouting();
                        s.AddAuthentication();
                        s.AddSingleton<TraxMarker>();
                        s.AddSingleton(Substitute.For<ITrainDiscoveryService>());
                        s.AddSingleton(Substitute.For<IEffectRegistry>());
                        s.AddSingleton(Substitute.For<ITraxScheduler>());
                        s.AddSingleton(Substitute.For<IOperationsService>());
                        s.AddScoped(_ => Substitute.For<IJobSubmitter>());
                        s.AddSingleton(Substitute.For<ITrainExecutionService>());
                        s.AddSingleton(Substitute.For<ITraxHealthService>());
                        s.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                effects.UsePostgres(PostgresFixture.ConnectionString)
                            )
                        );
                        // Shadow mode: the documented rollout step before enforcement is turned
                        // on. Inline documents run, and are logged, but ids still mean what the
                        // store says they mean.
                        s.AddTraxGraphQL(g =>
                            g.ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .AllowAnonymousOperations()
                                .AddTypeExtension<GraphQLFixture.HelloQuery>()
                                .UsePersistedOperations(po =>
                                    po.UseDatabase(PostgresFixture.ConnectionString)
                                        .SingleNode()
                                        .RequirePersisted(requirePersisted)
                                        .LogNonPersistedRequests()
                                )
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UsePersistedOperationsEnforcement();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .StartAsync();

        return (
            host,
            host.GetTestClient(),
            host.Services.GetRequiredService<IPersistedOperationStore>()
        );
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [SetUp]
    public async Task SetUp() => await PostgresFixture.ClearAsync();

    [Test]
    public async Task AnIdTheStoreDoesNotHold_DoesNotRunADocumentAnotherCallerSentWithIt()
    {
        // One caller sends a document of its own together with an id nobody uploaded.
        await PostAsync(new { id = "GetGreeting", query = "query Unknown { version }" });

        // The next caller asks for that id, as a client built against it does.
        var reply = await PostAsync(new { id = "GetGreeting" });

        reply
            .TryGetProperty("data", out var data)
            .Should()
            .BeFalse(
                "an id the store does not hold must be refused, not run another caller's document (Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md): {0}",
                reply
            );
        data.ValueKind.Should().NotBe(JsonValueKind.Object);
    }

    [Test]
    public async Task ADeactivatedId_DoesNotRunADocumentAnotherCallerSentWithIt()
    {
        await _store.UpsertAsync(
            "RetiredGreeting",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );
        await _store.DeactivateAsync("RetiredGreeting", null, "retired", CancellationToken.None);

        await PostAsync(new { id = "RetiredGreeting", query = "query Retired { version }" });
        var reply = await PostAsync(new { id = "RetiredGreeting" });

        reply
            .TryGetProperty("data", out var data)
            .Should()
            .BeFalse(
                "a deactivated id must stay refused, not run another caller's document (Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md): {0}",
                reply
            );
        data.ValueKind.Should().NotBe(JsonValueKind.Object);
    }

    [Test]
    public async Task UnderEnforcement_AnIdTheStoreDoesNotHold_DoesNotRunADocumentSentWithItOverGet()
    {
        var (host, client, _) = await StartAsync(requirePersisted: true);
        using (host)
        using (client)
        {
            // Sent over GET, with the document next to an id nobody uploaded.
            await client.GetAsync(
                "/trax/graphql?id=NextGreeting&query="
                    + Uri.EscapeDataString("query Next { version }")
            );

            // A client asks for the id over POST, which enforcement inspects and lets through
            // because it carries no inline document.
            var reply = await PostAsync(client, new { id = "NextGreeting" });

            reply
                .TryGetProperty("data", out var data)
                .Should()
                .BeFalse(
                    "an id the store does not hold must be refused, not run another caller's document (Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md): {0}",
                    reply
                );
            data.ValueKind.Should().NotBe(JsonValueKind.Object);
            await host.StopAsync();
        }
    }

    [Test]
    public async Task AnIdSentWithADocumentThatIsNotItsHash_IsRefusedWithACode()
    {
        var reply = await PostAsync(
            new { id = "Mismatched", query = "query Mismatched { version }" }
        );

        reply
            .TryGetProperty("data", out _)
            .Should()
            .BeFalse(
                "an id runs only its stored document (Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md): {0}",
                reply
            );
        reply
            .GetProperty("errors")[0]
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("PERSISTED_OPERATION_ID_MISMATCH");
    }

    [Test]
    public async Task AnIdTheStoreHolds_SentWithAnotherDocument_IsRefusedRatherThanRunEither()
    {
        await _store.UpsertAsync(
            "HeldGreeting",
            GraphQLFixture.ValidDocument,
            null,
            CancellationToken.None
        );

        var reply = await PostAsync(new { id = "HeldGreeting", query = "query Other { version }" });

        reply
            .TryGetProperty("data", out _)
            .Should()
            .BeFalse(
                "an id runs only its stored document (Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md): {0}",
                reply
            );
    }

    [Test]
    public async Task AnIdThatIsTheDocumentsOwnHash_RunsThatDocument()
    {
        // The automatic-persisted-query shape: the id is the hash of the document sent with it,
        // under the executor's hash algorithm (MD5 hex by default).
        const string document = "query OwnHash { version }";
        var hash = new MD5DocumentHashProvider(HashFormat.Hex)
            .ComputeHash(Encoding.UTF8.GetBytes(document))
            .Value;

        var reply = await PostAsync(new { id = hash, query = document });

        reply.GetProperty("data").GetProperty("version").GetString().Should().Be("v1");

        // And the id then names that same document, because the id is its content.
        var again = await PostAsync(new { id = hash });
        again.GetProperty("data").GetProperty("version").GetString().Should().Be("v1");
    }

    private Task<JsonElement> PostAsync(object body) => PostAsync(_client, body);

    private static async Task<JsonElement> PostAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/trax/graphql", body);
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
