using System.Security.Claims;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A policy named by an <c>@authorize</c> directive anywhere in the schema must be registered:
/// the host refuses to start otherwise, naming the policy and the field. Should a directive still
/// reach a request with an unknown policy, the caller is refused with the uniform
/// <c>TRAX_AUTHORIZATION</c> error and the policy's name stays out of the response. A train's
/// <c>[TraxAuthorize(Policy = ...)]</c>, which no directive carries, is held to the same rule.
/// <para>Enforces <c>docs/adr/0001-a-misconfigured-host-fails-at-startup.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-a-misconfigured-host-fails-at-startup.md")]
[TestFixture]
public class SchemaAuthorizationPolicyTests
{
    private const string Adr = "docs/adr/0001-a-misconfigured-host-fails-at-startup.md";

    [Test]
    public async Task AResolverNamingAnUnregisteredPolicy_RefusesTheHost_NamingPolicyAndField()
    {
        var ex = await StartAsync(g => g.AddTypeExtension<UnregisteredPolicyOnPublicThing>());

        ex.Should().NotBeNull("a field no caller could ever pass must not ship, per " + Adr);
        ex!
            .ToString()
            .Should()
            .Contain("NoSuchResolverPolicy")
            .And.Contain("PublicThing.unregisteredPolicyField");
    }

    [Test]
    public async Task GateOperationsNamingAnUnregisteredPolicy_RefusesTheHost()
    {
        var ex = await StartAsync(g =>
            g.ExposeOperationQueries().GateOperations(policy: "NoSuchOpsPolicy")
        );

        ex.Should().NotBeNull();
        ex!.ToString().Should().Contain("NoSuchOpsPolicy").And.Contain(".operations");
    }

    [Test]
    public async Task EveryNamedPolicyRegistered_TheHostStarts()
    {
        var ex = await StartAsync(
            g =>
                g.ExposeOperationQueries()
                    .GateOperations(policy: "OpsPolicy")
                    .AddTypeExtension<RegisteredPolicyOnPublicThing>(),
            policies: ["OpsPolicy", "RegisteredResolverPolicy"]
        );

        ex.Should().BeNull();
    }

    [Test]
    public async Task AnUnknownPolicyAtRequestTime_IsRefusedAsTraxAuthorization_WithoutItsName()
    {
        // The startup gate is not run here: the host is never started, so the request reaches a
        // directive whose policy does not exist.
        await using var provider = BaseServices(g =>
                g.ExposeOperationQueries().GateOperations(policy: "NoSuchOpsPolicy")
            )
            .BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument("{ operations { __typename } }")
                .SetUser(
                    new ClaimsPrincipal(
                        new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "Test")
                    )
                )
                .Build()
        );

        var json = result.ToJson();
        json.Should().Contain("TRAX_AUTHORIZATION");
        json.Should().NotContain("NoSuchOpsPolicy", "the name describes how the host is built");
        json.Should().NotContain("AUTH_POLICY_NOT_FOUND");
    }

    [Test]
    public async Task ATrainNamingAnUnregisteredPolicy_RefusesTheHost_NamingPolicyAndTrain()
    {
        var ex = await StartAsync(_ => { }, trains: [GatedTrain("NoSuchTrainPolicy")]);

        ex.Should().NotBeNull("a train no caller could ever pass must not ship, per " + Adr);
        ex!
            .ToString()
            .Should()
            .Contain("NoSuchTrainPolicy")
            .And.Contain(typeof(IPolicyGatedTrain).FullName);
    }

    [Test]
    public async Task ATrainNamingARegisteredPolicy_TheHostStarts()
    {
        var ex = await StartAsync(
            _ => { },
            policies: ["TrainPolicy"],
            trains: [GatedTrain("TrainPolicy")]
        );

        ex.Should().BeNull();
    }

    public interface IPolicyGatedTrain;

    private static TrainRegistration GatedTrain(string policy) =>
        new()
        {
            ServiceType = typeof(IPolicyGatedTrain),
            ImplementationType = typeof(object),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = nameof(IPolicyGatedTrain),
            ImplementationTypeName = "PolicyGatedTrain",
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [policy],
            RequiredRoles = [],
            HasAuthorizeAttribute = true,
            HasAllowAnonymousAttribute = false,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private static async Task<Exception?> StartAsync(
        Action<TraxGraphQLBuilder> configure,
        string[]? policies = null,
        TrainRegistration[]? trains = null
    )
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        foreach (var descriptor in BaseServices(configure, policies, trains))
            builder.Services.Add(descriptor);
        using var host = builder.Build();

        try
        {
            await host.StartAsync();
            await host.StopAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static ServiceCollection BaseServices(
        Action<TraxGraphQLBuilder> configure,
        string[]? policies = null,
        TrainRegistration[]? trains = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(o =>
        {
            foreach (var policy in policies ?? [])
                o.AddPolicy(policy, p => p.RequireAuthenticatedUser());
        });
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(trains ?? []);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddDbContextFactory<CensusDbContext>(o =>
            o.UseInMemoryDatabase("policy-" + Guid.NewGuid())
        );
        services.AddTraxGraphQL(graphql =>
        {
            graphql.AddDbContext<CensusDbContext>();
            configure(graphql);
            return graphql;
        });

        // Backing services for the operations resolvers, so exposing the namespace does not fail
        // for an unrelated reason.
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<IOperationsService>());
        services.AddScoped(_ =>
            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
        );
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
        services.AddScoped(_ => Substitute.For<IJobSubmitter>());
        return services;
    }
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class UnregisteredPolicyOnPublicThing
{
    [TraxAuthorize(Policy = "NoSuchResolverPolicy")]
    public string UnregisteredPolicyField([Parent] PublicThing thing) => thing.Name;
}

[ExtendObjectType(typeof(PublicThing))]
public sealed class RegisteredPolicyOnPublicThing
{
    [TraxAuthorize(Policy = "RegisteredResolverPolicy")]
    public string RegisteredPolicyField([Parent] PublicThing thing) => thing.Name;
}
