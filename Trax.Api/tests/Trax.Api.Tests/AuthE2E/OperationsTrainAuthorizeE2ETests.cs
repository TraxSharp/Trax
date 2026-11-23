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
                        ConfigureHostServices(services, connectionString)
                    )
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

    private void ConfigureHostServices(IServiceCollection services, string connectionString)
    {
        services.AddLogging();
        services.AddRouting();
        // A second scheme, so there is no default one and each request's
        // user comes from the scheme that authenticated it.
        if (withApiKeys)
            services.AddTraxApiKeyAuth(keys =>
                keys.Add("ops-train-authorize-key-at-least-32-characters", id: "ops", "Staff")
            );
        services.AddTraxJwtAuth(jwt =>
            jwt.UseSymmetricKey(AuthE2EHost.JwtIssuer, AuthE2EHost.JwtAudience, AuthE2EHost.JwtKey)
        );
        services.AddAuthorization(o => o.AddPolicy("AdminPolicy", p => p.RequireRole("Admin")));
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
        var (run, sku) = await CustomerFailedOrderAsync();

        using var doc = await PostAsync(RequeueExecution(run), _staff.NotNull());

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
        (await QueuedWithAsync(sku)).Should().Be(0, "a refused requeue queues nothing");

        // A caller in both the gate's role and the train's may.
        using var allowed = await PostAsync(RequeueExecution(run), _staffCustomer.NotNull());
        ErrorCodes(allowed).Should().BeEmpty(allowed.RootElement.GetRawText());
        allowed
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("requeueExecution")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(allowed.RootElement.GetRawText());
        (await QueuedWithAsync(sku)).Should().Be(1, "the allowed requeue queues the run's input");
    }

    private static string RequeueExecution(long run) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""mutation { operations { requeueExecution(id: {{run}}) { success message } } }"""
        );

    [Test]
    public async Task QueueTrain_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        var sku = Guid.NewGuid().ToString("N");
        var before = await LastRunIdAsync();

        using var doc = await PostAsync(QueueTrain(sku), _staff.NotNull());

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
        (await QueuedWithAsync(sku)).Should().Be(0, "a refused queueTrain queues nothing");
        (await DirectRunsAfterAsync(before)).Should().Be(0, "a refused queueTrain starts no run");

        // A caller in both the gate's role and the train's may.
        using var allowed = await PostAsync(QueueTrain(sku), _staffCustomer.NotNull());
        Succeeded(allowed, "workQueue", "queueTrain");
        (await QueuedWithAsync(sku))
            .Should()
            .BeGreaterThan(0, "the allowed queueTrain queues the input");
    }

    [Test]
    public async Task RunTrain_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        var sku = Guid.NewGuid().ToString("N");
        var before = await LastRunIdAsync();

        using var doc = await PostAsync(RunTrain(sku), _staff.NotNull());

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
        (await DirectRunsAfterAsync(before)).Should().Be(0, "a refused runTrain writes no run");
        (await QueuedWithAsync(sku)).Should().Be(0, "a refused runTrain queues nothing");

        // A caller in both the gate's role and the train's may.
        using var allowed = await PostAsync(RunTrain(sku), _staffCustomer.NotNull());
        Succeeded(allowed, "workQueue", "runTrain");
        (await DirectRunsAfterAsync(before)).Should().Be(1, "the allowed runTrain writes its run");
    }

    private static string QueueTrain(string sku) =>
        $$"""mutation { operations { workQueue { queueTrain(input: { trainName: "{{TrainName}}", inputJson: "{\"sku\":\"{{sku}}\"}" }) { success message } } } }""";

    private static string RunTrain(string sku) =>
        $$"""mutation { operations { workQueue { runTrain(input: { trainName: "{{TrainName}}", inputJson: "{\"sku\":\"{{sku}}\"}" }) { success message } } } }""";

    /// <summary>The mutation at <paramref name="path"/> under <c>operations</c> succeeded.</summary>
    private static void Succeeded(JsonDocument response, params string[] path)
    {
        var raw = response.RootElement.GetRawText();
        ErrorCodes(response).Should().BeEmpty(raw);
        var field = response.RootElement.GetProperty("data").GetProperty("operations");
        foreach (var name in path)
            field = field.GetProperty(name);
        field.GetProperty("success").GetBoolean().Should().BeTrue(raw);
    }

    [Test]
    public async Task ResumeExecution_ByStaffOutsideTheTrainsRole_IsRefused()
    {
        // A run that checkpointed before it failed, so the resume gets as far as the enqueue,
        // where the train's [TraxAuthorize] applies.
        var run = await CheckpointedFailedOrderAsync();

        using var doc = await PostAsync(ResumeExecution(run), _staff.NotNull());

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION", doc.RootElement.GetRawText());
        (await ResumesOfAsync(run)).Should().Be(0, "a refused resume queues nothing");

        // A caller in both the gate's role and the train's may.
        using var allowed = await PostAsync(ResumeExecution(run), _staffCustomer.NotNull());
        Succeeded(allowed, "resumeExecution");
        (await ResumesOfAsync(run)).Should().Be(1, "the allowed resume queues one entry");
    }

    private static string ResumeExecution(long run) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""mutation { operations { resumeExecution(id: {{run}}) { success message } } }"""
        );

    /// <summary>
    /// A run of <see cref="ICheckpointedOrderTrain"/> that wrote its checkpoint and then failed.
    /// Run in process, as a worker runs it: the resume is what is under test.
    /// </summary>
    private async Task<long> CheckpointedFailedOrderAsync()
    {
        long run;
        var scope = _host.NotNull().Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var train = scope.ServiceProvider.GetRequiredService<ICheckpointedOrderTrain>();
            var act = async () =>
                await train
                    .Run(new PlaceOrderInput { Sku = Guid.NewGuid().ToString("N") })
                    .ConfigureAwait(false);
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            run = ((ServiceTrain<PlaceOrderInput, PlaceOrderOutput>)train).Metadata.NotNull().Id;
        }

        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            (await db.Metadatas.AsNoTracking().SingleAsync(m => m.Id == run).ConfigureAwait(false))
                .TrainState.Should()
                .Be(TrainState.Failed);
            (
                await db
                    .Checkpoints.AsNoTracking()
                    .CountAsync(c => c.MetadataId == run)
                    .ConfigureAwait(false)
            )
                .Should()
                .Be(1, "the run checkpointed before it failed");
            return run;
        }
    }

    private async Task<int> ResumesOfAsync(long run)
    {
        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.WorkQueues.CountAsync(q => q.ResumeFrom == run).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A customer places an order through the train's own mutation, and it fails. Returns the run
    /// and the order's sku, which no other run's input carries.
    /// </summary>
    private async Task<(long Run, string Sku)> CustomerFailedOrderAsync()
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
            return (run.Id, sku);
        }
    }

    /// <summary>
    /// The work queue rows whose input carries <paramref name="sku"/>. Counted by the test's own
    /// sku rather than the train, because the fixture's database outlives a run of the suite.
    /// </summary>
    private async Task<int> QueuedWithAsync(string sku)
    {
        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            // The column is jsonb, so it is matched as text.
            return await ((DbContext)db)
                .Database.SqlQuery<int>(
                    $"SELECT count(*)::int AS \"Value\" FROM trax.work_queue WHERE input::text LIKE {"%" + sku + "%"}"
                )
                .SingleAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>The newest run's id, so a check can look only at runs written after it.</summary>
    private async Task<long> LastRunIdAsync()
    {
        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await ((DbContext)db)
                .Database.SqlQuery<long>(
                    $"SELECT coalesce(max(id), 0) AS \"Value\" FROM trax.metadata"
                )
                .SingleAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The runs of the train written after <paramref name="after"/> by a direct run rather than
    /// the dispatcher: a dispatched run carries its work queue row's external id, and the
    /// scheduler may dispatch another test's queued row at any time.
    /// </summary>
    private async Task<int> DirectRunsAfterAsync(long after)
    {
        var db = await _host
            .NotNull()
            .Services.GetRequiredService<IDataContextProviderFactory>()
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await ((DbContext)db)
                .Database.SqlQuery<int>(
                    $"""
                    SELECT count(*)::int AS "Value" FROM trax.metadata m
                    WHERE m.name = {TrainName} AND m.id > {after}
                      AND NOT EXISTS (SELECT 1 FROM trax.work_queue q WHERE q.external_id = m.external_id)
                    """
                )
                .SingleAsync()
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
