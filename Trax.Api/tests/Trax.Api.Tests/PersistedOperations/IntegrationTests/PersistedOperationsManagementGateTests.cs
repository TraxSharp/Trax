using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// The persisted-operation management fields sit at the root, behind a gate of their own that
/// <c>GateManagement</c> declares, and turning on persisted operations adds nothing to the
/// <c>operations</c> namespace. A deploy pipeline that uploads documents holds the management
/// role and none of the operator's.
///
/// <para>Enforces <c>docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md")]
[TestFixture]
[Category("Integration")]
public class PersistedOperationsManagementGateTests
{
    private const string Adr =
        "docs/adr/0004-the-operations-namespace-gates-independently-of-the-endpoint.md";

    private const string OperatorKey = "management-gate-operator";
    private const string DeployerKey = "management-gate-deployer";
    private const string ReaderKey = "management-gate-reader";

    private const string Upload = """
        mutation Upload($input: UploadPersistedOperationInput!) {
          persistedOperations {
            uploadPersistedOperation(input: $input) { success errors { code } }
          }
        }
        """;

    private const string List = "{ persistedOperations { persistedOperations { totalCount } } }";

    private const string Cancel = "mutation { operations { cancelExecution(id: 1) { success } } }";

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        if (!PostgresFixture.IsPostgresReachable())
            Assert.Ignore("Postgres not reachable; skipping integration tests.");

        await DatabaseMigrator.Migrate(PostgresFixture.ConnectionString);
    }

    [SetUp]
    public async Task SetUp() => await PostgresFixture.ClearAsync();

    [Test]
    public async Task UsePersistedOperations_with_default_options_adds_no_operations_namespace()
    {
        await using var app = await StartAsync(g => g, po => po.SingleNode());
        var schema = (await ExecutorAsync(app)).Schema;

        RootFields(schema)
            .Should()
            .NotContain(
                "operations",
                "turning on persisted operations publishes none of the admin namespace, per " + Adr
            )
            .And.NotContain(
                "persistedOperations",
                "with no management gate declared the management fields are not served"
            );
    }

    [Test]
    public async Task Management_fields_exist_only_once_their_gate_is_declared()
    {
        await using (var ungated = await StartAsync(g => g, po => po.SingleNode()))
        {
            RootFields((await ExecutorAsync(ungated)).Schema)
                .Should()
                .NotContain("persistedOperations", "the fields fail closed without a gate");
        }

        await using var gated = await StartAsync(
            g => g,
            po => po.SingleNode().GateManagement(roles: "Deployer")
        );
        var schema = (await ExecutorAsync(gated)).Schema;

        schema.QueryType.Fields.Select(f => f.Name).Should().Contain("persistedOperations");
        schema.MutationType!.Fields.Select(f => f.Name).Should().Contain("persistedOperations");
        RootFields(schema)
            .Should()
            .NotContain("operations", "a management gate exposes nothing under operations");
    }

    [Test]
    public async Task A_caller_with_the_management_role_but_not_the_operator_role_may_upload_and_may_not_cancel_an_execution()
    {
        await using var app = await StartAsync(
            graphql => graphql.ExposeOperationMutations().GateOperations(roles: "Operator"),
            po => po.SingleNode().GateManagement(roles: "Deployer")
        );

        var upload = await PostAsync(app, DeployerKey, Upload, UploadVariables("deploy_v1"));
        upload
            .RootElement.GetProperty("data")
            .GetProperty("persistedOperations")
            .GetProperty("uploadPersistedOperation")
            .GetProperty("success")
            .GetBoolean()
            .Should()
            .BeTrue(upload.RootElement.ToString());

        var cancel = await PostAsync(app, DeployerKey, Cancel);
        HasErrorCode(cancel, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(
                "the management role reaches none of the operations namespace, per "
                    + Adr
                    + ": "
                    + cancel.RootElement
            );

        var operatorUpload = await PostAsync(
            app,
            OperatorKey,
            Upload,
            UploadVariables("operator_v1")
        );
        HasErrorCode(operatorUpload, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("the operator's role is not the management gate");
    }

    [Test]
    public async Task An_anonymous_caller_cannot_reach_management()
    {
        await using var app = await StartAsync(
            g => g,
            po => po.SingleNode().GateManagement(roles: "Deployer")
        );

        HasErrorCode(await PostAsync(app, apiKey: null, List), "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue();
        HasErrorCode(
                await PostAsync(app, apiKey: null, Upload, UploadVariables("anonymous_v1")),
                "TRAX_AUTHORIZATION"
            )
            .Should()
            .BeTrue();
        HasErrorCode(await PostAsync(app, ReaderKey, List), "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue("a signed-in caller without the role is refused too");
    }

    [Test]
    public void GateManagement_without_policy_or_roles_throws()
    {
        var builder = new PersistedOperationsBuilder();

        builder
            .Invoking(b => b.GateManagement())
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*GateManagementToAuthenticatedUsers()*");
    }

    [Test]
    public void GateManagement_with_a_role_list_of_only_separators_throws()
    {
        var builder = new PersistedOperationsBuilder();

        builder
            .Invoking(b => b.GateManagement(roles: " , ,"))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*names no role*");
    }

    [Test]
    public async Task GateManagement_naming_an_unregistered_policy_fails_startup()
    {
        var start = () =>
            StartAsync(
                g => g,
                po => po.SingleNode().GateManagement(policy: "NoSuchManagementPolicy")
            );

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain("NoSuchManagementPolicy")
            .And.Contain("persistedOperations");
    }

    private static Dictionary<string, object?> UploadVariables(string id) =>
        new()
        {
            ["input"] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["document"] = GraphQLFixture.ValidDocument,
            },
        };

    private static IEnumerable<string> RootFields(ISchemaDefinition schema) =>
        schema
            .QueryType.Fields.Select(f => f.Name)
            .Concat(schema.MutationType?.Fields.Select(f => f.Name) ?? []);

    private static async Task<WebApplication> StartAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> graphql,
        Action<PersistedOperationsBuilder> persisted
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );
        builder.WebHost.UseTestServer();
        builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
        var services = builder.Services;
        services.AddLogging();
        services.AddRouting();
        services.AddTraxApiKeyAuth(keys =>
            keys.Add(OperatorKey, id: "operator", "Operator")
                .Add(DeployerKey, id: "deployer", "Deployer")
                .Add(ReaderKey, id: "reader", "Reader")
        );
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<IOperationsService>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddSingleton(Substitute.For<IJobSubmitter>());
        services.AddSingleton(
            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
        );
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(PostgresFixture.ConnectionString))
        );
        // A query model, so the schema has a query root with neither operations nor management.
        services.AddDbContextFactory<CensusDbContext>(o =>
            o.UseInMemoryDatabase("management-gate-" + Guid.NewGuid())
        );
        // Inline documents run, so what refuses a request below is authorization and not
        // persisted-operation enforcement.
        services.AddTraxGraphQL(g =>
            graphql(g)
                .AddDbContext<CensusDbContext>()
                .AddTypeExtension<GraphQLFixture.HelloQuery>()
                .UsePersistedOperations(po =>
                {
                    po.RequirePersisted(false).LogNonPersistedRequests(true);
                    persisted(po);
                })
        );

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseTraxGraphQL();
        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
        return app;
    }

    private static Task<IRequestExecutor> ExecutorAsync(WebApplication app) =>
        app
            .Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax")
            .AsTask();

    private static async Task<JsonDocument> PostAsync(
        WebApplication app,
        string? apiKey,
        string query,
        Dictionary<string, object?>? variables = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        var response = await app.GetTestClient().SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static bool HasErrorCode(JsonDocument doc, string code) =>
        doc.RootElement.TryGetProperty("errors", out var errors)
        && errors
            .EnumerateArray()
            .Any(e =>
                e.TryGetProperty("extensions", out var ext)
                && ext.TryGetProperty("code", out var c)
                && c.GetString() == code
            );
}
