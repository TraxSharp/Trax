using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Auth.Jwt;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// The operations mutations that start a train apply the train's own <c>[TraxAuthorize]</c> on
/// top of the operations gate, on a host as an application builds one: JWT, the real scheduler and
/// the real operations service over Postgres. A caller past the gate but outside the train's role
/// is refused on every one of them.
/// </summary>
[TestFixture(false)]
[TestFixture(true)]
[NonParallelizable]
[Property("adr", "Trax.Docs/adr/0017-a-callers-enqueue-goes-through-the-mediator.md")]
public class OperationsTrainAuthorizeE2ETests(bool withApiKeys)
{
    private string Database => "trax_api_operations_train_authorize" + (withApiKeys ? "_keys" : "");
    private static readonly string TrainName = typeof(IPlaceOrderTrain).FullName.NotNull();

    private IHost? _host;
    private string? _customer;
    private string? _staff;
    private string? _staffCustomer;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost
            .ConnectionString(Database)
            .Replace("Maximum Pool Size=8", "Maximum Pool Size=20", StringComparison.Ordinal);

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        // A second scheme, so there is no default one and each request's
                        // user comes from the scheme that authenticated it.
                        if (withApiKeys)
                            services.AddTraxApiKeyAuth(keys =>
                                keys.Add(
                                    "ops-train-authorize-key-at-least-32-characters",
                                    id: "ops",
                                    "Staff"
                                )
                            );
                        services.AddTraxJwtAuth(jwt =>
                            jwt.UseSymmetricKey(
                                AuthE2EHost.JwtIssuer,
                                AuthE2EHost.JwtAudience,
                                AuthE2EHost.JwtKey
                            )
                        );
                        services.AddAuthorization(o =>
                            o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin"))
                        );
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects
                                        .UsePostgres(connectionString)
                                        .SaveTrainParameters()
                                        .AddDecisionRecording()
                                )
                                .AddMediator(typeof(AuthE2EHost).Assembly)
                                .AddScheduler(scheduler => scheduler)
                        );
                        services.AddTraxApi();
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .GateOperations(roles: "Staff")
                        );
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
        await _host.NotNull().StartAsync();

        _customer = AuthE2EHost.SignJwt("customer-1", "Customer One", "Customer");
        _staff = AuthE2EHost.SignJwt("staff-1", "Staff One", "Staff");
        _staffCustomer = AuthE2EHost.SignJwt("staff-2", "Staff Two", "Staff", "Customer");
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        await _host.NotNull().StopAsync();
        _host?.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task RequeueExecution_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        var run = await CustomerFailedOrderAsync();

        using var doc = await PostAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""mutation { operations { requeueExecution(id: {{run}}) { success message } } }"""
            ),
            _staff.NotNull()
        );

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
        (await QueuedEntriesAsync()).Should().Be(0, "a refused requeue queues nothing");

        // A caller in both the gate's role and the train's may.
        using var allowed = await PostAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""mutation { operations { requeueExecution(id: {{run}}) { success message } } }"""
            ),
            _staffCustomer.NotNull()
        );
        ErrorCodes(allowed).Should().BeEmpty(allowed.RootElement.GetRawText());
        allowed
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("requeueExecution")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(allowed.RootElement.GetRawText());
    }

    [Test]
    public async Task QueueTrain_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        using var doc = await PostAsync(
            $$"""mutation { operations { workQueue { queueTrain(input: { trainName: "{{TrainName}}", inputJson: "{\"sku\":\"x\"}" }) { success message } } } }""",
            _staff.NotNull()
        );

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
    }

    [Test]
    public async Task RunTrain_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        using var doc = await PostAsync(
            $$"""mutation { operations { workQueue { runTrain(input: { trainName: "{{TrainName}}", inputJson: "{\"sku\":\"x\"}" }) { success message } } } }""",
            _staff.NotNull()
        );

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
    }

    [Test]
    public async Task ResumeExecution_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        var run = await CustomerFailedOrderAsync();

        using var doc = await PostAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""mutation { operations { resumeExecution(id: {{run}}) { success message } } }"""
            ),
            _staff.NotNull()
        );

        // A run with no checkpoint is refused for that reason too; what must not happen is a
        // queued resume.
        var raw = doc.RootElement.GetRawText();
        if (!ErrorCodes(doc).Contains("TRAX_AUTHORIZATION"))
            doc.RootElement.GetProperty("data")
                .GetProperty("operations")
                .GetProperty("resumeExecution")
                .GetProperty("success")
                .GetBoolean()
                .Should()
                .BeFalse(raw);
    }

    /// <summary>A customer places an order through the train's own mutation, and it fails.</summary>
    private async Task<long> CustomerFailedOrderAsync()
    {
        var sku = Guid.NewGuid().ToString("N");
        using var doc = await PostAsync(
                $$"""mutation { dispatch { shop { placeOrder(input: { sku: "{{sku}}" }) { externalId } } } }""",
                _customer.NotNull()
            )
            .ConfigureAwait(false);
        ErrorCodes(doc).Should().NotContain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());

        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // Found by its input: a requeued run the scheduler dispatched may be newer.
            var id = await ((DbContext)db)
                .Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM trax.metadata WHERE input::text LIKE {"%" + sku + "%"}"
                )
                .SingleAsync()
                .ConfigureAwait(false);
            var run = await db
                .Metadatas.AsNoTracking()
                .Where(m => m.Id == id)
                .Select(m => new { m.Id, m.TrainState })
                .SingleAsync()
                .ConfigureAwait(false);
            run.TrainState.Should().Be(TrainState.Failed);

            // The run records its starter, so a requeue keeps it and enqueues through the mediator's
            // options overload, as the reported requeue did.
            var starter = await ((DbContext)db)
                .Database.SqlQuery<string?>(
                    $"SELECT starter_hash AS \"Value\" FROM trax.metadata WHERE id = {run.Id}"
                )
                .SingleAsync()
                .ConfigureAwait(false);
            starter.Should().NotBeNull("a GraphQL run records its caller as the starter");
            return run.Id;
        }
    }

    private async Task<int> QueuedEntriesAsync()
    {
        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db
                .WorkQueues.CountAsync(q => q.TrainName == TrainName)
                .ConfigureAwait(false);
        }
    }

    private Task<JsonDocument> PostAsync(string query, string token) =>
        _host
            .NotNull()
            .PostGraphQLAsync(query, r => r.Headers.Authorization = new("Bearer", token));

    private static List<string?> ErrorCodes(JsonDocument response) =>
        response.RootElement.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var x) && x.TryGetProperty("code", out var c)
                        ? c.GetString()
                        : null
                )
                .ToList()
            : [];
}
