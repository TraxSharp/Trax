using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// The operator's read-only view of state-machine instances over HTTP, against a host that gates
/// the operations namespace to a role: an operator reads the list, one instance and the counts
/// through the shared Scheduler service; an anonymous caller and a signed-in caller without the
/// role get nothing, user-owned drafts included; and no type the view returns has a field for a
/// snapshot's context or its owner's key.
/// </summary>
[Property("adr", "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md")]
[TestFixture]
public class MachineInstanceQueriesOverHttpTests
{
    private const string OperatorKey = "machines-operator-key";
    private const string UserKey = "machines-user-key";
    private const string Machine = "Acme.Fulfilment";

    private static readonly Guid InstanceId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly MachineInstanceRecord UserDraft = new(
        42,
        Machine,
        SnapshotOwnerKind.User,
        InstanceId,
        "AwaitingPayment",
        2,
        Created,
        Created.AddMinutes(3),
        HasLiveInvokedRun: true
    );

    private const string List =
        "{ operations { machineInstances(machine: \"Acme.Fulfilment\", state: \"AwaitingPayment\", "
        + "ownerKind: USER, skip: 0, take: 10) { totalCount isCountCapped skip take items "
        + "{ rowId machine ownerKind id state version createdAt updatedAt hasLiveInvokedRun } } } }";

    private const string One =
        "{ operations { machineInstance(machine: \"Acme.Fulfilment\", ownerKind: USER, "
        + "id: \"6f9619ff-8b86-d011-b42d-00c04fc964ff\", rowId: 42) "
        + "{ rowId machine ownerKind id state hasLiveInvokedRun } } }";

    private const string Counts =
        "{ operations { machineInstanceCounts(machine: \"Acme.Fulfilment\") "
        + "{ machine state ownerKind count } } }";

    private const string Cancel =
        "mutation { operations { cancelMachineInstance(machine: \"Acme.Fulfilment\", "
        + "ownerKind: SYSTEM, id: \"6f9619ff-8b86-d011-b42d-00c04fc964ff\") "
        + "{ success outcome message state } } }";

    private static readonly MachineInstanceRun LiveRun = new(
        7,
        "run-external-id",
        "Acme.IShipTrain",
        Trax.Effect.Enums.TrainState.InProgress,
        new DateTime(2026, 10, 1, 9, 1, 0, DateTimeKind.Utc),
        null,
        Trax.Core.Exceptions.FailureClass.Unclassified,
        CancellationRequested: false,
        IsLive: true
    );

    private IHost _host = null!;
    private IOperationsService _operations = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _operations = Substitute.For<IOperationsService>();
        _operations
            .GetMachineInstancesAsync(Arg.Any<MachineInstanceQuery>(), Arg.Any<CancellationToken>())
            .Returns(new MachineInstancePage([UserDraft], 0, 10));
        _operations
            .CountMachineInstancesAsync(
                Arg.Any<MachineInstanceQuery>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new MachineInstanceTotal(1, Capped: false));
        _operations
            .GetMachineInstanceAsync(Arg.Any<MachineInstanceKey>(), Arg.Any<CancellationToken>())
            .Returns(UserDraft);
        _operations
            .GetMachineInstanceRunsAsync(
                Arg.Any<MachineInstanceKey>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new MachineInstanceRuns([LiveRun], Capped: false, QueuedEntryId: null));
        _operations
            .CancelMachineInstanceAsync(Arg.Any<MachineInstanceKey>(), Arg.Any<CancellationToken>())
            .Returns(
                new MachineInstanceCancelResult(
                    MachineInstanceCancelOutcome.Moved,
                    "moved",
                    "Cancelled"
                )
            );
        _operations
            .GetMachineInstanceStateCountsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([
                new MachineInstanceStateCount(
                    Machine,
                    "AwaitingPayment",
                    SnapshotOwnerKind.User,
                    7
                ),
                new MachineInstanceStateCount(Machine, "Shipped", SnapshotOwnerKind.System, 3),
            ]);

        _host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddTraxApiKeyAuth(keys =>
                            keys.Add(OperatorKey, id: "operator", "Operator")
                                .Add(UserKey, id: "shopper", "Shopper")
                        );
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .ExposeOperationMutations()
                                .GateOperations(roles: "Operator")
                        );
                        services.AddScoped(_ => _operations);
                        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
                        services.AddScoped(_ => Substitute.For<ITrainExecutionService>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Scheduler.Services.JobSubmitter.IJobSubmitter>()
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

        await _host.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private static IEnumerable<TestCaseData> Documents()
    {
        yield return new TestCaseData(List).SetName("machineInstances");
        yield return new TestCaseData(One).SetName("machineInstance");
        yield return new TestCaseData(Counts).SetName("machineInstanceCounts");
        yield return new TestCaseData(Cancel).SetName("cancelMachineInstance");
    }

    [TestCaseSource(nameof(Documents))]
    public async Task An_anonymous_caller_gets_nothing(string document)
    {
        var doc = await PostAsync(document, apiKey: null);

        doc.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        NoOperationsData(doc).Should().BeTrue(doc.RootElement.GetRawText());
        doc.RootElement.GetRawText().Should().NotContain(InstanceId.ToString());
    }

    [TestCaseSource(nameof(Documents))]
    public async Task A_signed_in_caller_without_the_operations_role_gets_nothing(string document)
    {
        var doc = await PostAsync(document, UserKey);

        Auth.AdminOperationsAuthorizationTests.HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(doc.RootElement.GetRawText());
        NoOperationsData(doc)
            .Should()
            .BeTrue("a user's own draft is not listed to them, or anyone, without the role");
        doc.RootElement.GetRawText().Should().NotContain(InstanceId.ToString());
    }

    [TestCaseSource(nameof(Documents))]
    public async Task An_operator_is_answered(string document)
    {
        var doc = await PostAsync(document, OperatorKey);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
    }

    [Test]
    public async Task The_list_is_the_services_page_and_total_with_the_filters_passed_through()
    {
        var page = Operations(await PostAsync(List, OperatorKey), "machineInstances");

        page.GetProperty("totalCount").GetInt32().Should().Be(1);
        page.GetProperty("isCountCapped").GetBoolean().Should().BeFalse();
        var item = page.GetProperty("items")[0];
        item.GetProperty("rowId").GetInt64().Should().Be(42);
        item.GetProperty("ownerKind").GetString().Should().Be("USER");
        item.GetProperty("id").GetString().Should().Be(InstanceId.ToString());
        item.GetProperty("state").GetString().Should().Be("AwaitingPayment");
        item.GetProperty("version").GetInt32().Should().Be(2);
        item.GetProperty("hasLiveInvokedRun").GetBoolean().Should().BeTrue();
        await _operations
            .Received()
            .GetMachineInstancesAsync(
                new MachineInstanceQuery(Machine, "AwaitingPayment", SnapshotOwnerKind.User, 0, 10),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task One_instance_is_looked_up_by_owner_kind_and_row_id()
    {
        var instance = Operations(await PostAsync(One, OperatorKey), "machineInstance");

        instance.GetProperty("rowId").GetInt64().Should().Be(42);
        await _operations
            .Received()
            .GetMachineInstanceAsync(
                new MachineInstanceKey(Machine, SnapshotOwnerKind.User, InstanceId, 42),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task The_detail_lists_the_services_invoked_runs()
    {
        var instance = Operations(
            await PostAsync(
                "{ operations { machineInstance(machine: \"Acme.Fulfilment\", ownerKind: USER, "
                    + "id: \"6f9619ff-8b86-d011-b42d-00c04fc964ff\", rowId: 42) "
                    + "{ invokedRuns { id externalId name trainState startTime endTime failureClass "
                    + "cancellationRequested isLive } isInvokedRunsCapped queuedInvokedRunEntryId } } }",
                OperatorKey
            ),
            "machineInstance"
        );

        var run = instance.GetProperty("invokedRuns")[0];
        run.GetProperty("id").GetInt64().Should().Be(7);
        run.GetProperty("name").GetString().Should().Be("Acme.IShipTrain");
        run.GetProperty("trainState").GetString().Should().Be("IN_PROGRESS");
        run.GetProperty("isLive").GetBoolean().Should().BeTrue();
        instance.GetProperty("isInvokedRunsCapped").GetBoolean().Should().BeFalse();
        await _operations
            .Received()
            .GetMachineInstanceRunsAsync(
                new MachineInstanceKey(Machine, SnapshotOwnerKind.User, InstanceId, 42),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task An_operators_cancel_is_the_services_result_for_the_system_instance()
    {
        var doc = await PostAsync(Cancel, OperatorKey);

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        var result = doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("cancelMachineInstance");
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("outcome").GetString().Should().Be("MOVED");
        result.GetProperty("message").GetString().Should().Be("moved");
        result.GetProperty("state").GetString().Should().Be("Cancelled");
        await _operations
            .Received()
            .CancelMachineInstanceAsync(
                new MachineInstanceKey(Machine, SnapshotOwnerKind.System, InstanceId),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(null)]
    [TestCase(UserKey)]
    public async Task A_caller_without_the_operations_role_cannot_cancel(string? apiKey)
    {
        _operations.ClearReceivedCalls();

        var doc = await PostAsync(Cancel, apiKey);

        doc.RootElement.TryGetProperty("errors", out _).Should().BeTrue();
        NoOperationsData(doc).Should().BeTrue(doc.RootElement.GetRawText());
        await _operations
            .DidNotReceive()
            .CancelMachineInstanceAsync(
                Arg.Any<MachineInstanceKey>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task A_users_draft_looked_up_without_its_row_id_is_refused()
    {
        var doc = await PostAsync(
            "{ operations { machineInstance(machine: \"Acme.Fulfilment\", ownerKind: USER, "
                + "id: \"6f9619ff-8b86-d011-b42d-00c04fc964ff\") { rowId } } }",
            OperatorKey
        );

        Auth.AdminOperationsAuthorizationTests.HasErrorCode(doc, "TRAX_ROW_ID_REQUIRED")
            .Should()
            .BeTrue(doc.RootElement.GetRawText());
    }

    [Test]
    public async Task The_counts_are_the_services_counts()
    {
        var counts = Operations(await PostAsync(Counts, OperatorKey), "machineInstanceCounts");

        counts.GetArrayLength().Should().Be(2);
        counts[0].GetProperty("state").GetString().Should().Be("AwaitingPayment");
        counts[0].GetProperty("count").GetInt64().Should().Be(7);
        counts[1].GetProperty("ownerKind").GetString().Should().Be("SYSTEM");
    }

    [TestCase("MachineInstance")]
    [TestCase("MachineInstanceDetail")]
    [TestCase("MachineInstanceCount")]
    [TestCase("MachineInstanceInvokedRun")]
    [TestCase("MachineInstanceCancelResponse")]
    public async Task No_type_the_view_returns_has_a_context_or_an_owner_key(string typeName)
    {
        var executor = await _host
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var type = executor.Schema.Types.OfType<ObjectType>().Single(t => t.Name == typeName);

        type.Fields.Select(f => f.Name)
            .Should()
            .NotContain(
                name =>
                    name.Contains("context", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("snapshot", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("userKey", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("invokeToken", StringComparison.OrdinalIgnoreCase),
                "operators see an instance's state, timestamps, owner kind and runs, never its "
                    + "context (central docs/0046), and no operator view names the user behind a row"
            );
    }

    private static bool NoOperationsData(JsonDocument doc) =>
        !doc.RootElement.TryGetProperty("data", out var data)
        || data.ValueKind == JsonValueKind.Null
        || data.GetProperty("operations").ValueKind == JsonValueKind.Null;

    private static JsonElement Operations(JsonDocument doc, string field)
    {
        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        return doc.RootElement.GetProperty("data").GetProperty("operations").GetProperty(field);
    }

    private async Task<JsonDocument> PostAsync(string query, string? apiKey)
    {
        var client = _host.GetTestServer().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);
        var response = await client.SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
