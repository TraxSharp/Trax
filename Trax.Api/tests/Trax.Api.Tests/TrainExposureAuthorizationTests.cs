using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// Exposure authorization for trains, enforced in <c>AddTraxGraphQL</c>. A
/// <c>[TraxQuery]</c>/<c>[TraxMutation]</c> train must declare <c>[TraxAuthorize]</c> or
/// <c>[TraxAllowAnonymous]</c> (never both); <c>[TraxAllowAnonymous]</c> is contradictory once
/// the endpoint is gated via <c>RequireAuthorization()</c>. The decision matrix itself is pinned
/// in <see cref="ExposureAuthorizationRuleTests"/>; these tests verify the wiring: that the
/// validation runs, reads the endpoint posture, and lists every offending train.
/// </summary>
[TestFixture]
public class TrainExposureAuthorizationTests
{
    /// <summary>
    /// Builds the minimal service collection AddTraxGraphQL needs (the AddTrax marker plus a
    /// stubbed train list) and returns the AddTraxGraphQL call so the test can assert on it.
    /// The exposure check runs before any HotChocolate wiring, so no further scaffolding is needed.
    /// </summary>
    private static Action AddGraphQL(IReadOnlyList<TrainRegistration> trains, bool gated)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton<ITrainDiscoveryService>(new StubDiscovery(trains));
        return () => services.AddTraxGraphQL(gated ? b => b.RequireAuthorization() : b => b);
    }

    [Test]
    public void OpenEndpoint_BareQueryTrain_Throws()
    {
        var act = AddGraphQL([Train<IBareTrain>(isQuery: true)], gated: false);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*neither [TraxAuthorize] nor [TraxAllowAnonymous]*")
            .WithMessage($"*{typeof(IBareTrain).FullName}*");
    }

    [Test]
    public void OpenEndpoint_BareMutationTrain_Throws()
    {
        var act = AddGraphQL([Train<IBareTrain>(isQuery: false)], gated: false);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(IBareTrain).FullName}*");
    }

    [Test]
    public void OpenEndpoint_AuthorizeTrain_Succeeds()
    {
        var act = AddGraphQL([Train<IGatedTrain>(isQuery: true, hasAuthorize: true)], gated: false);

        act.Should().NotThrow();
    }

    [Test]
    public void OpenEndpoint_AllowAnonymousTrain_Succeeds()
    {
        var act = AddGraphQL(
            [Train<IAnonTrain>(isQuery: true, hasAllowAnonymous: true)],
            gated: false
        );

        act.Should().NotThrow();
    }

    [Test]
    public void AnyEndpoint_TrainWithBothMarkers_ThrowsConflict()
    {
        var act = AddGraphQL(
            [Train<IConflictTrain>(isQuery: true, hasAuthorize: true, hasAllowAnonymous: true)],
            gated: false
        );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*mutually exclusive*")
            .WithMessage($"*{typeof(IConflictTrain).FullName}*");
    }

    [Test]
    public void GatedEndpoint_BareTrain_Succeeds()
    {
        var act = AddGraphQL([Train<IBareTrain>(isQuery: true)], gated: true);

        act.Should().NotThrow();
    }

    [Test]
    public void GatedEndpoint_AuthorizeTrain_Succeeds()
    {
        var act = AddGraphQL([Train<IGatedTrain>(isQuery: true, hasAuthorize: true)], gated: true);

        act.Should().NotThrow();
    }

    [Test]
    public void GatedEndpoint_AllowAnonymousTrain_Throws()
    {
        var act = AddGraphQL(
            [Train<IAnonTrain>(isQuery: true, hasAllowAnonymous: true)],
            gated: true
        );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*RequireAuthorization*")
            .WithMessage($"*{typeof(IAnonTrain).FullName}*");
    }

    [Test]
    public void OpenEndpoint_NonExposedBareTrain_Ignored()
    {
        // A train that is neither query nor mutation is not GraphQL-exposed, so the rule does
        // not apply even with no marker. Pair it with an exposed, declared train to keep the
        // schema's query root non-empty.
        var act = AddGraphQL(
            [
                Train<IBareTrain>(isQuery: false, isMutation: false),
                Train<IAnonTrain>(isQuery: true, hasAllowAnonymous: true),
            ],
            gated: false
        );

        act.Should().NotThrow();
    }

    [Test]
    public void OpenEndpoint_MultipleOffenders_AllListed()
    {
        var act = AddGraphQL(
            [Train<IBareTrain>(isQuery: true), Train<IGatedTrain>(isQuery: false)],
            gated: false
        );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exposure authorization check failed*")
            .WithMessage($"*{typeof(IBareTrain).FullName}*")
            .WithMessage($"*{typeof(IGatedTrain).FullName}*");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static TrainRegistration Train<TService>(
        bool isQuery = false,
        bool isMutation = true,
        bool hasAuthorize = false,
        bool hasAllowAnonymous = false
    )
    {
        // A train is a query or a mutation, never both; isQuery wins when set.
        var query = isQuery;
        var mutation = !isQuery && isMutation;

        return new TrainRegistration
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(TService),
            InputType = typeof(StubInput),
            OutputType = typeof(StubOutput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TService).Name,
            InputTypeName = nameof(StubInput),
            OutputTypeName = nameof(StubOutput),
            RequiredPolicies = [],
            RequiredRoles = [],
            HasAuthorizeAttribute = hasAuthorize,
            HasAllowAnonymousAttribute = hasAllowAnonymous,
            IsQuery = query,
            IsMutation = mutation,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = GraphQLOperation.Run,
        };
    }

    private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private interface IBareTrain;

    private interface IGatedTrain;

    private interface IAnonTrain;

    private interface IConflictTrain;

    private record StubInput
    {
        public string Value { get; init; } = "";
    }

    private record StubOutput
    {
        public string Result { get; init; } = "";
    }
}
