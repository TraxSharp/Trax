using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Authorization;
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
using Trax.Api.GraphQL.PersistedOperations.Services;
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

    [Test]
    public async Task A_host_root_field_named_persistedOperations_is_refused_once_management_is_served()
    {
        // Without the gate the name is the host's: nothing of this package is at the root.
        await using (
            var ungated = await StartAsync(
                g => g.ConfigureSchema(b => b.AddTypeExtension<HostPersistedOperationsOnRoot>()),
                po => po.SingleNode()
            )
        )
        {
            RootFields((await ExecutorAsync(ungated)).Schema)
                .Should()
                .Contain("persistedOperations");
        }

        var start = () =>
            StartAsync(
                g => g.ConfigureSchema(b => b.AddTypeExtension<HostPersistedOperationsOnRoot>()),
                po => po.SingleNode().GateManagement(roles: "Deployer")
            );

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain("RootQuery.persistedOperations")
            .And.Contain(typeof(HostResolvers).FullName!);
    }

    [Test]
    public async Task A_policy_with_a_scoped_handler_is_evaluated_in_the_callers_scope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(o =>
            o.AddPolicy(ScopedPolicy, p => p.AddRequirements(new ScopedRequirement()))
        );
        // A handler that needs something per request, such as a DbContext.
        services.AddScoped<IAuthorizationHandler, ScopedRequirementHandler>();
        services.AddPersistedOperationStore(store =>
            store.SingleNode().GateManagement(policy: ScopedPolicy)
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using var scope = provider.CreateAsyncScope();

        var gate = scope.ServiceProvider.GetRequiredService<IPersistedOperationsManagementGate>();

        (await gate.AllowsAsync(Principals["deployer"]))
            .Should()
            .BeTrue(
                "the gate evaluates the policy in the caller's scope, where GraphQL evaluates the "
                    + "fields' policy, so a scoped handler is resolved there"
            );
    }

    [Test]
    public async Task The_gate_and_the_persistedOperations_fields_admit_the_same_principals()
    {
        // Gate, then for each principal whether both surfaces admit it.
        var table = new (
            string Gate,
            Action<PersistedOperationsBuilder> Declare,
            string[] Admitted
        )[]
        {
            (
                "roles Deployer",
                po => po.GateManagement(roles: "Deployer"),
                ["deployer", "anonymous-first-deployer"]
            ),
            (
                "roles Deployer or Releaser, and Auditor",
                po =>
                    po.GateManagement(roles: "Deployer,Releaser").GateManagement(roles: "Auditor"),
                ["releaser-auditor"]
            ),
            (
                "policy alone",
                po => po.GateManagement(policy: ClaimPolicy),
                ["can-manage", "anonymous-first-deployer"]
            ),
            (
                "authenticated only",
                po => po.GateManagementToAuthenticatedUsers(),
                [
                    "deployer",
                    "reader",
                    "releaser-auditor",
                    "can-manage",
                    "authenticated",
                    "anonymous-first-deployer",
                ]
            ),
        };

        var mismatches = new List<string>();
        foreach (var (gateName, declare, admitted) in table)
        {
            await using var app = await StartAsync(
                g => g,
                po => declare(po.SingleNode()),
                services =>
                    services.AddAuthorization(o =>
                        o.AddPolicy(ClaimPolicy, p => p.RequireClaim("can-manage"))
                    ),
                withTestPrincipals: true
            );

            foreach (var (name, principal) in Principals)
            {
                var expected = admitted.Contains(name);

                var response = await PostAsPrincipalAsync(app, name, List);
                var graphql = !response.RootElement.TryGetProperty("errors", out _);

                await using var scope = app.Services.CreateAsyncScope();
                var gate = await scope
                    .ServiceProvider.GetRequiredService<IPersistedOperationsManagementGate>()
                    .AllowsAsync(principal);

                if (graphql != expected || gate != expected)
                    mismatches.Add(
                        $"{gateName}, {name}: expected {expected}, GraphQL {graphql}, gate {gate}"
                    );
            }
        }

        mismatches
            .Should()
            .BeEmpty(
                "the dashboard's gate and the GraphQL persistedOperations fields refuse the same "
                    + "people"
            );
    }

    [Test]
    public async Task A_store_registered_after_UsePersistedOperations_does_not_change_the_gate_or_enforcement()
    {
        foreach (var storeFirst in new[] { false, true })
        {
            var order = storeFirst ? "store first" : "store last";
            Action<IServiceCollection> differentGate = s =>
                s.AddPersistedOperationStore(store =>
                    store.SingleNode().GateManagementToAuthenticatedUsers()
                );

            var refused = () =>
                StartAsync(
                    g => g,
                    po => po.SingleNode().GateManagement(roles: "Deployer"),
                    storeFirst ? differentGate : null,
                    configureAfter: storeFirst ? null : differentGate
                );
            (await refused.Should().ThrowAsync<Exception>(order))
                .Which.ToString()
                .Should()
                .Contain("AddPersistedOperationStore declares the management gate")
                .And.Contain("UsePersistedOperations declares (roles 'Deployer')");

            Action<IServiceCollection> sameGate = s =>
                s.AddPersistedOperationStore(store =>
                    store.SingleNode().GateManagement(roles: "Deployer")
                );
            await using var app = await StartAsync(
                g => g,
                po => po.SingleNode().GateManagement(roles: "Deployer"),
                storeFirst ? sameGate : null,
                configureAfter: storeFirst ? null : sameGate
            );

            app.Services.GetServices<PersistedOperationsOptions>()
                .Should()
                .ContainSingle(order + ": one process has one set of options");
            app.Services.GetRequiredService<PersistedOperationsOptions>()
                .RequirePersisted.Should()
                .BeFalse(
                    order
                        + ": UsePersistedOperations turned enforcement off, and the store "
                        + "registration does not turn it back on"
                );
        }
    }

    private const string ScopedPolicy = "ManagesThroughAScopedHandler";
    private const string ClaimPolicy = "HoldsCanManage";
    private const string PrincipalHeader = "X-Test-Principal";

    private static readonly Dictionary<string, ClaimsPrincipal> Principals = new()
    {
        ["deployer"] = Authenticated(new Claim(ClaimTypes.Role, "Deployer")),
        ["reader"] = Authenticated(new Claim(ClaimTypes.Role, "Reader")),
        ["releaser-auditor"] = Authenticated(
            new Claim(ClaimTypes.Role, "Releaser"),
            new Claim(ClaimTypes.Role, "Auditor")
        ),
        ["can-manage"] = Authenticated(new Claim("can-manage", "true")),
        ["authenticated"] = Authenticated(),
        // An identity with no authentication type is not authenticated, whatever it claims.
        ["unauthenticated-deployer"] = new ClaimsPrincipal(
            new ClaimsIdentity([
                new Claim(ClaimTypes.Role, "Deployer"),
                new Claim("can-manage", "true"),
            ])
        ),
        ["anonymous-first-deployer"] = new ClaimsPrincipal([
            new ClaimsIdentity(),
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, "Deployer"), new Claim("can-manage", "true")],
                "Test"
            ),
        ]),
    };

    private static ClaimsPrincipal Authenticated(params Claim[] claims) =>
        new(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "someone"), .. claims], "Test")
        );

    private sealed class ScopedRequirement : IAuthorizationRequirement;

    private sealed class ScopedRequirementHandler : AuthorizationHandler<ScopedRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ScopedRequirement requirement
        )
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    private static async Task<JsonDocument> PostAsPrincipalAsync(
        WebApplication app,
        string principal,
        string query
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(new { query }),
        };
        request.Headers.Add(PrincipalHeader, principal);

        var response = await app.GetTestClient().SendAsync(request);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // Built with a descriptor rather than [ExtendObjectType], so an assembly scan does not pick it up.
    public sealed class HostPersistedOperationsOnRoot : ObjectTypeExtension
    {
        protected override void Configure(IObjectTypeDescriptor descriptor)
        {
            descriptor.Name("RootQuery");
            descriptor.Field<HostResolvers>(r => r.PersistedOperations());
        }
    }

    public sealed class HostResolvers
    {
        [Trax.Effect.Attributes.TraxAllowAnonymous]
        public string PersistedOperations() => "the host's own";
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
        Action<PersistedOperationsBuilder> persisted,
        Action<IServiceCollection>? configure = null,
        bool withTestPrincipals = false,
        Action<IServiceCollection>? configureAfter = null
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
        configure?.Invoke(services);
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
        configureAfter?.Invoke(services);

        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        if (withTestPrincipals)
            // The principal a request names, as an authentication handler would have set it.
            app.Use(
                (context, next) =>
                {
                    if (
                        context.Request.Headers.TryGetValue(PrincipalHeader, out var name)
                        && Principals.TryGetValue(name.ToString(), out var principal)
                    )
                        context.User = principal;
                    return next(context);
                }
            );
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
