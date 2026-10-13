using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// A train's route and its GraphQL exposure are two registrations: a train with no <c>[TraxMutation]</c> is routed
/// and not exposed, and a <see cref="TrainMutationExposure"/> registered beside it exposes it as the attribute would,
/// gated by the exposure's policy.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TrainDiscoveryExposureTests
{
    private static TrainRegistration Discover(TrainMutationExposure? exposure)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRoutedTrain, RoutedTrain>();
        services.AddScoped<RoutedTrain>();
        if (exposure is not null)
            services.AddSingleton(exposure);
        return new TrainDiscoveryService(services)
            .DiscoverTrains()
            .Single(r => r.ImplementationType == typeof(RoutedTrain));
    }

    [Test]
    public void A_train_without_an_exposure_is_routed_and_not_a_mutation()
    {
        var registration = Discover(exposure: null);

        registration.IsMutation.Should().BeFalse();
        registration.RequiredPolicies.Should().BeEmpty();
    }

    [Test]
    public void An_exposure_makes_the_train_a_mutation_behind_its_policy()
    {
        var registration = Discover(
            new TrainMutationExposure(
                typeof(RoutedTrain),
                new TraxMutationAttribute(GraphQLOperation.Run)
                {
                    Namespace = "tooling",
                    Description = "Exposed by registration.",
                },
                "tooling-policy"
            )
        );

        registration.IsMutation.Should().BeTrue();
        registration.GraphQLNamespace.Should().Be("tooling");
        registration.GraphQLOperations.Should().Be(GraphQLOperation.Run);
        registration.GraphQLDescription.Should().Be("Exposed by registration.");
        registration.HasAuthorizeAttribute.Should().BeTrue();
        registration.RequiredPolicies.Should().Equal("tooling-policy");
    }

    public sealed record In;

    public sealed record Out;

    public interface IRoutedTrain : IServiceTrain<In, Out>;

    public class RoutedTrain : ServiceTrain<In, Out>, IRoutedTrain
    {
        protected override Task<Either<Exception, Out>> Junctions() =>
            Task.FromResult<Either<Exception, Out>>(new Out());
    }
}
