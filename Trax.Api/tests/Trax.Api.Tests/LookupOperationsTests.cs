using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.ChainVerification;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// <c>operations.lookup</c> over HTTP: a user's draft found by its GUID is shown only to a caller
/// the operations gate lets list drafts, the rule <c>machineInstances</c> follows, and even then
/// without its context or whose it is.
/// </summary>
[TestFixture]
public class LookupOperationsTests
{
    private const string AdminKey = "lookup-admin-key";
    private const string ReaderKey = "lookup-reader-key";

    private IHost _host = null!;
    private IDataContextProviderFactory _factory = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                        {
                            keys.Add(AdminKey, id: "admin", "Admin");
                            keys.Add(ReaderKey, id: "reader", "Reader");
                        });
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().GateOperations(roles: "Admin")
                        );
                        services.AddSingleton(Substitute.For<ITrainChainGraphs>());
                        services.AddSingleton(_factory);
                        services.AddScoped<IOperationsService>(_ => new OperationsService(
                            discovery,
                            _factory,
                            new SchedulerConfiguration(),
                            Substitute.For<ITrainExecutionService>()
                        ));
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => Substitute.For<ITrainExecutionService>());
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Test]
    public async Task AUsersDraft_FoundByItsGuid_IsShownOnlyToACallerWhoMayListDrafts()
    {
        var id = Guid.NewGuid();
        var userKey = "user-" + Guid.NewGuid().ToString("N");
        var secret = "secret-" + Guid.NewGuid().ToString("N");
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            db.SnapshotDrafts.Add(
                new SnapshotDraft
                {
                    Id = id,
                    OwnerKind = SnapshotOwnerKind.User,
                    UserKey = userKey,
                    Machine = "topic-map",
                    Version = 1,
                    State = "Building",
                    Context = JsonSerializer.Serialize(new { apiKey = secret }),
                    ConcurrencyToken = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }
            );
            await db.SaveChanges(default);
        }

        var lookup =
            $"{{ operations {{ lookup(text: \"{id}\") {{ kind id label matchedOn machine ownerKind rowId }} }} }}";
        const string list = "{ operations { machineInstances { items { id } } } }";

        // A caller the gate refuses the instance list to is refused the lookup the same way.
        foreach (var query in new[] { lookup, list })
        {
            using var refused = await PostAsync(query, ReaderKey);
            refused
                .RootElement.TryGetProperty("errors", out _)
                .Should()
                .BeTrue(refused.RootElement.GetRawText());
            refused.RootElement.GetRawText().Should().NotContain(id.ToString());
        }

        using var shown = await PostAsync(lookup, AdminKey);
        shown
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(shown.RootElement.GetRawText());
        var match = shown
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("lookup")
            .EnumerateArray()
            .Should()
            .ContainSingle()
            .Which;
        match.GetProperty("kind").GetString().Should().Be("MACHINE_INSTANCE");
        match.GetProperty("id").GetString().Should().Be(id.ToString());
        match.GetProperty("ownerKind").GetString().Should().Be("USER");
        match.GetProperty("machine").GetString().Should().Be("topic-map");
        match.GetProperty("rowId").ValueKind.Should().Be(JsonValueKind.Number);
        shown.RootElement.GetRawText().Should().NotContain(userKey).And.NotContain(secret);
    }

    private async Task<JsonDocument> PostAsync(string query, string key)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        request.Headers.Add("X-Api-Key", key);
        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
