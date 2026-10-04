using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// Who may receive the lifecycle and data-change subscriptions, decided from the operations gate
/// and each broadcast train's own posture: policies, roles, an authenticated caller, or the
/// endpoint gate for a train that declares nothing.
///
/// <para>Enforces <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md")]
[TestFixture]
public class LifecycleSubscriptionAccessTests
{
    private const string Adr =
        "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md";

    private const string OpsPolicy = "Ops";
    private const string TrainPolicy = "Billing";

    #region The operations gate

    [Test]
    public async Task OperationsGatedByPolicy_AdmitsOnlyACallerMeetingIt()
    {
        var access = Access(b => b.ExposeOperationQueries().GateOperations(policy: OpsPolicy));

        (await access.LifecycleFor(User(claims: "ops"))).All.Should().BeTrue();
        (await access.DataChangesFor(User(claims: "ops"))).Should().BeTrue();

        (await access.LifecycleFor(User()))
            .IsEmpty.Should()
            .BeTrue("a caller failing the operations policy sees no train, per " + Adr);
        (await access.DataChangesFor(User())).Should().BeFalse();
    }

    [Test]
    public async Task OperationsGatedByRole_AdmitsOnlyACallerInIt()
    {
        var access = Access(b => b.ExposeOperationQueries().GateOperations(roles: "Admin"));

        (await access.LifecycleFor(User(roles: "Admin"))).All.Should().BeTrue();
        (await access.LifecycleFor(User(roles: "Player"))).All.Should().BeFalse();
    }

    [Test]
    public async Task OperationsGate_RefusesAnAnonymousOrIdentitylessCaller()
    {
        var access = Access(b => b.ExposeOperationQueries().GateOperations(roles: "Admin"));

        (await access.DataChangesFor(null)).Should().BeFalse();
        (await access.DataChangesFor(new ClaimsPrincipal())).Should().BeFalse();
        (await access.DataChangesFor(new ClaimsPrincipal(new ClaimsIdentity())))
            .Should()
            .BeFalse("an identity with no authentication type is not signed in");
    }

    #endregion

    #region A broadcast train's own posture

    [Test]
    public async Task BroadcastTrainWithAPolicy_ReachesOnlyACallerMeetingIt()
    {
        var access = Access(trains: Train<IPolicyTrain>(policies: [TrainPolicy]));

        (await access.LifecycleFor(User(claims: "billing")))
            .Trains.Should()
            .Contain(typeof(IPolicyTrain).FullName!);
        (await access.LifecycleFor(User()))
            .IsEmpty.Should()
            .BeTrue("the train's policy is not met, per " + Adr);
    }

    [Test]
    public async Task BroadcastTrainWithABareAuthorize_ReachesAnySignedInCaller()
    {
        var access = Access(trains: Train<IPolicyTrain>());

        (await access.LifecycleFor(User())).Trains.Should().Contain(typeof(IPolicyTrain).FullName!);
        (await access.LifecycleFor(null)).IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task BroadcastTrainWithNoPosture_ReachesASignedInCallerOnlyBehindTheEndpointGate()
    {
        var undeclared = Train<IUndeclaredTrain>(authorize: false);

        (await Access(b => b.RequireAuthorization(), undeclared).LifecycleFor(User()))
            .Trains.Should()
            .Contain(
                typeof(IUndeclaredTrain).FullName!,
                "the endpoint policy is the gate of a train that declares none, per " + Adr
            );
        (await Access(trains: undeclared).LifecycleFor(User())).IsEmpty.Should().BeTrue();
    }

    #endregion

    private interface IPolicyTrain;

    private interface IUndeclaredTrain;

    private static LifecycleSubscriptionAccess Access(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? configure = null,
        params TrainRegistration[] trains
    )
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        configure?.Invoke(builder);

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(trains);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(o =>
        {
            o.AddPolicy(OpsPolicy, p => p.RequireClaim("ops"));
            o.AddPolicy(TrainPolicy, p => p.RequireClaim("billing"));
        });

        return new LifecycleSubscriptionAccess(
            builder.Build(),
            discovery,
            services.BuildServiceProvider().GetRequiredService<IAuthorizationService>(),
            new TrainLifecycleStreamOptions()
        );
    }

    private static TrainRegistration Train<TService>(
        IReadOnlyList<string>? policies = null,
        bool authorize = true
    ) =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TService),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TService).Name,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = policies ?? [],
            RequiredRoles = [],
            HasAuthorizeAttribute = authorize,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = true,
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private static ClaimsPrincipal User(string? roles = null, string? claims = null)
    {
        var identity = new ClaimsIdentity("Test");
        foreach (var role in (roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        foreach (var claim in (claims ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            identity.AddClaim(new Claim(claim, "yes"));
        return new ClaimsPrincipal(identity);
    }
}
