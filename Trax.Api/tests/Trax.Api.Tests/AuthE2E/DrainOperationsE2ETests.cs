using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.Jwt;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// The console's Drain and Resume as the API serves them (Trax.Docs/adr/0065), on a host as an application builds
/// one: JWT, the real scheduler and the operations service over Postgres, the operations namespace gated to a role.
/// An operator drains, reads it back and resumes; a caller outside the gate can do none of it and changes nothing.
/// </summary>
[TestFixture]
[NonParallelizable]
public class DrainOperationsE2ETests
{
    private const string Database = "trax_api_drain_operations";

    private const string Status =
        "query { operations { config { drain { drained until startedBy busyTotal busy { kind id name state } } } } }";

    private IHost? _host;
    private string? _operator;
    private string? _customer;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        AuthE2EHost.EnsureDatabaseExists(Database);
        var connectionString = AuthE2EHost.ConnectionString(Database);

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
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
                                .GateOperations(roles: "Operator")
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

        _operator = AuthE2EHost.SignJwt("ops-1", "Ops One", "Operator");
        _customer = AuthE2EHost.SignJwt("customer-1", "Customer One", "Customer");
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        await _host.NotNull().StopAsync();
        _host?.Dispose();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    [SetUp]
    public async Task NoDrain()
    {
        var factory = _host.NotNull().Services.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await using var disposal = context.ConfigureAwait(false);
        await context.Drains.ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task NoDrainAfter() => await NoDrain();

    [Test]
    public async Task An_operator_drains_reads_it_back_and_resumes()
    {
        using var drained = await PostAsync(
            "mutation { operations { config { drain(minutes: 30) { success count message } } } }",
            _operator.NotNull()
        );
        Field(drained, "drain").GetProperty("success").GetBoolean().Should().BeTrue(Raw(drained));

        using var status = await PostAsync(Status, _operator.NotNull());
        var drain = Config(status).GetProperty("drain");
        drain.GetProperty("drained").GetBoolean().Should().BeTrue(Raw(status));
        drain
            .GetProperty("startedBy")
            .GetString()
            .Should()
            .Contain("ops-1", "the drain records the operator who set it");
        drain.GetProperty("busyTotal").GetInt32().Should().Be(0);

        using var resumed = await PostAsync(
            "mutation { operations { config { resume { success count message } } } }",
            _operator.NotNull()
        );
        Field(resumed, "resume").GetProperty("count").GetInt32().Should().Be(1, Raw(resumed));

        using var after = await PostAsync(Status, _operator.NotNull());
        Config(after).GetProperty("drain").GetProperty("drained").GetBoolean().Should().BeFalse();
    }

    [TestCase(0)]
    [TestCase(1441)]
    public async Task A_duration_outside_a_minute_to_a_day_is_refused(int minutes)
    {
        using var refused = await PostAsync(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"mutation {{ operations {{ config {{ drain(minutes: {minutes}) {{ success message }} }} }} }}"
            ),
            _operator.NotNull()
        );

        Field(refused, "drain").GetProperty("success").GetBoolean().Should().BeFalse(Raw(refused));
        (await DrainRows()).Should().Be(0);
    }

    [TestCase("mutation { operations { config { drain { success } } } }")]
    [TestCase("mutation { operations { config { resume { success } } } }")]
    [TestCase(Status)]
    public async Task A_caller_outside_the_operations_gate_is_refused_and_changes_nothing(
        string document
    )
    {
        using var drained = await PostAsync(
            "mutation { operations { config { drain(minutes: 30) { success } } } }",
            _operator.NotNull()
        );

        using var refused = await PostAsync(document, _customer.NotNull());

        ErrorCodes(refused).Should().Contain("TRAX_AUTHORIZATION", Raw(refused));
        (await DrainRows())
            .Should()
            .Be(1, "a refused resume leaves the drain, and a refused drain sets nothing new");
    }

    [Test]
    public async Task An_anonymous_caller_is_refused()
    {
        using var refused = await _host
            .NotNull()
            .PostGraphQLAsync("mutation { operations { config { drain { success } } } }");

        ErrorCodes(refused).Should().NotBeEmpty(Raw(refused));
        (await DrainRows()).Should().Be(0);
    }

    private async Task<int> DrainRows()
    {
        var factory = _host.NotNull().Services.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var disposal = context.ConfigureAwait(false);
        return await context.Drains.CountAsync().ConfigureAwait(false);
    }

    private Task<JsonDocument> PostAsync(string query, string token) =>
        _host
            .NotNull()
            .PostGraphQLAsync(query, r => r.Headers.Authorization = new("Bearer", token));

    private static JsonElement Config(JsonDocument doc) =>
        doc.RootElement.GetProperty("data").GetProperty("operations").GetProperty("config");

    private static JsonElement Field(JsonDocument doc, string name) =>
        Config(doc).GetProperty(name);

    private static string Raw(JsonDocument doc) => doc.RootElement.GetRawText();

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
