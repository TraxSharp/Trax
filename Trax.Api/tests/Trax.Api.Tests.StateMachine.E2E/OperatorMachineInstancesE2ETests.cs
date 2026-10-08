using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// The operator's view of state-machine instances against a real draft: a user saves and advances
/// a draft through the <c>stateMachine</c> mutations, and then the operations namespace, gated to
/// an operator role, lists it to the operator and to nobody else (not to an anonymous caller,
/// and not to the user who owns it), and never returns the draft's context.
/// </summary>
[TestFixture]
[NonParallelizable]
public class OperatorMachineInstancesE2ETests
{
    private const string Database = "trax_statemachine_operator_e2e";
    private const string OperatorKey = "sm-operator-key";
    private const string ShopperKey = "sm-shopper-key";

    private IHost _host = null!;
    private Guid _draftId;

    private const string List = """
        { operations { machineInstances(machine: "turnstile") {
            totalCount items { rowId machine ownerKind id state version createdAt updatedAt hasLiveInvokedRun }
        } } }
        """;

    private const string Counts = """
        { operations { machineInstanceCounts(machine: "turnstile") { machine state ownerKind count } } }
        """;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _host = await StartAsync(E2EHost.ConnectionString(Database));

        // The shopper's own draft, advanced into a state whose context holds what it paid with.
        _draftId = Guid.NewGuid();
        using var save = await _host.PostAsync(
            """
            mutation Save($input: SaveSnapshotInput!) {
              dispatch { stateMachine { saveSnapshot(input: $input) { output { snapshot } } } }
            }
            """,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id = _draftId.ToString(),
                    snapshot = TurnstileMachine.InitialJson,
                },
            },
            ShopperKey
        );
        save.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(save.RootElement.GetRawText());
        using var advance = await _host.PostAsync(
            """
            mutation Advance($input: AdvanceSnapshotInput!) {
              dispatch { stateMachine { advanceSnapshot(input: $input) { output { snapshot } } } }
            }
            """,
            new
            {
                input = new
                {
                    machine = "turnstile",
                    id = _draftId.ToString(),
                    trigger = "Coin",
                    input = "{\"coin\":\"dollar\"}",
                },
            },
            ShopperKey
        );
        advance.RootElement.GetRawText().Should().Contain("Unlocked");
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [TestCase(List)]
    [TestCase(Counts)]
    public async Task An_anonymous_caller_is_not_shown_the_draft(string document)
    {
        using var doc = await _host.PostAsync(document);

        doc.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        NoOperationsData(doc).Should().BeTrue(doc.RootElement.GetRawText());
    }

    [TestCase(List)]
    [TestCase(Counts)]
    public async Task The_user_who_owns_the_draft_is_not_shown_it_without_the_operations_role(
        string document
    )
    {
        using var doc = await _host.PostAsync(document, apiKey: ShopperKey);

        ErrorCodes(doc).Should().Contain("TRAX_AUTHORIZATION");
        NoOperationsData(doc).Should().BeTrue(doc.RootElement.GetRawText());
        doc.RootElement.GetRawText().Should().NotContain(_draftId.ToString());
    }

    [Test]
    public async Task An_operator_sees_the_drafts_state_and_never_its_context()
    {
        using var doc = await _host.PostAsync(List, apiKey: OperatorKey);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        var page = doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("machineInstances");
        page.GetProperty("totalCount").GetInt32().Should().Be(1);
        var item = page.GetProperty("items")[0];
        item.GetProperty("id").GetString().Should().Be(_draftId.ToString());
        item.GetProperty("ownerKind").GetString().Should().Be("USER");
        item.GetProperty("state").GetString().Should().Be("Unlocked");
        item.GetProperty("createdAt").ValueKind.Should().Be(JsonValueKind.String);
        item.GetProperty("hasLiveInvokedRun").GetBoolean().Should().BeFalse();

        var raw = doc.RootElement.GetRawText();
        raw.Should()
            .NotContain("paidWith")
            .And.NotContain("dollar", "the context is never returned to an operator")
            .And.NotContain("shopper", "no operator view names the user behind a draft");

        using var one = await _host.PostAsync(
            $$"""
            { operations { machineInstance(machine: "turnstile", ownerKind: USER, id: "{{_draftId}}", rowId: {{item.GetProperty(
                "rowId"
            ).GetInt64()}}) { id state ownerKind } } }
            """,
            apiKey: OperatorKey
        );
        one.RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("machineInstance")
            .GetProperty("state")
            .GetString()
            .Should()
            .Be("Unlocked");

        using var asSystem = await _host.PostAsync(
            $$"""
            { operations { machineInstance(machine: "turnstile", ownerKind: SYSTEM, id: "{{_draftId}}") { id } } }
            """,
            apiKey: OperatorKey
        );
        asSystem
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("machineInstance")
            .ValueKind.Should()
            .Be(JsonValueKind.Null, "the user's draft is not a system instance under the same id");
    }

    private static async Task<IHost> StartAsync(string connectionString)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(OperatorKey, id: "operator", "Operator")
                                .Add(ShopperKey, id: "shopper", "Shopper")
                        );
                        services.AddAuthorization();
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    effects.UsePostgres(connectionString).AddJson()
                                )
                                .AddStateMachines(typeof(E2EHost).Assembly)
                                .AddMediator(typeof(E2EHost).Assembly)
                        );
                        services.AddScoped<ISnapshotPrincipal, TraxCallerSnapshotPrincipal>();
                        services.AddSingleton<IOrderCharge>(new CountingCharge());

                        // The operations service the dashboard and the API share, without the
                        // scheduler's workers: this host only reads.
                        services.AddScoped<IOperationsService>(sp => new OperationsService(
                            sp.GetRequiredService<ITrainDiscoveryService>(),
                            sp.GetRequiredService<IDataContextProviderFactory>(),
                            new SchedulerConfiguration(),
                            sp.GetRequiredService<ITrainExecutionService>()
                        ));

                        services.AddTraxApi();
                        services.AddTraxGraphQL(graphql =>
                            graphql.ExposeOperationQueries().GateOperations(roles: "Operator")
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                            endpoints.MapGraphQL("/trax/graphql", "trax")
                        );
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static bool NoOperationsData(JsonDocument doc) =>
        !doc.RootElement.TryGetProperty("data", out var data)
        || data.ValueKind == JsonValueKind.Null
        || data.GetProperty("operations").ValueKind == JsonValueKind.Null;

    private static IEnumerable<string?> ErrorCodes(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
            ? errors
                .EnumerateArray()
                .Select(e =>
                    e.TryGetProperty("extensions", out var ext)
                    && ext.TryGetProperty("code", out var code)
                        ? code.GetString()
                        : null
                )
            : [];
}
