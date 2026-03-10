using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

[TestFixture]
public class TrainDiscoveryServiceTests
{
    #region IsBroadcastEnabled

    [Test]
    public void DiscoverTrains_TrainWithSubscriptionAttribute_IsBroadcastEnabledTrue()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var subscription = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(SubscriptionTrain)
        );

        subscription.Should().NotBeNull();
        subscription!.IsBroadcastEnabled.Should().BeTrue();
    }

    [Test]
    public void DiscoverTrains_TrainWithoutSubscriptionAttribute_IsBroadcastEnabledFalse()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var plain = registrations.FirstOrDefault(r => r.ImplementationType == typeof(PlainTrain));

        plain.Should().NotBeNull();
        plain!.IsBroadcastEnabled.Should().BeFalse();
    }

    [Test]
    public void DiscoverTrains_TrainWithBothGraphQLAndSubscription_BothEnabled()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var both = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(GraphQLAndSubscriptionTrain)
        );

        both.Should().NotBeNull();
        both!.IsMutation.Should().BeTrue();
        both!.IsBroadcastEnabled.Should().BeTrue();
    }

    [Test]
    public void DiscoverTrains_TrainWithGraphQLOnly_SubscriptionDisabled()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var graphqlOnly = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(GraphQLOnlyTrain)
        );

        graphqlOnly.Should().NotBeNull();
        graphqlOnly!.IsMutation.Should().BeTrue();
        graphqlOnly!.IsBroadcastEnabled.Should().BeFalse();
    }

    [Test]
    public void DiscoverTrains_TrainWithSubscriptionOnly_GraphQLDisabled()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var subscription = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(SubscriptionTrain)
        );

        subscription.Should().NotBeNull();
        subscription!.IsBroadcastEnabled.Should().BeTrue();
        subscription!.IsMutation.Should().BeFalse();
    }

    #endregion

    #region Caching

    [Test]
    public void DiscoverTrains_CalledTwice_ReturnsSameInstance()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();

        var first = discovery.DiscoverTrains();
        var second = discovery.DiscoverTrains();

        first.Should().BeSameAs(second);
    }

    #endregion

    #region OutputType Discovery

    [Test]
    public void DiscoverTrains_UnitOutputTrain_OutputTypeIsUnit()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var plain = registrations.FirstOrDefault(r => r.ImplementationType == typeof(PlainTrain));

        plain.Should().NotBeNull();
        plain!.OutputType.Should().Be(typeof(Unit));
    }

    [Test]
    public void DiscoverTrains_TypedOutputTrain_OutputTypeIsCorrect()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var typed = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(TypedOutputTrain)
        );

        typed.Should().NotBeNull();
        typed!.OutputType.Should().Be(typeof(DiscoveryOutput));
    }

    [Test]
    public void DiscoverTrains_TypedOutputTrain_OutputTypeNameIsCorrect()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var typed = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(TypedOutputTrain)
        );

        typed.Should().NotBeNull();
        typed!.OutputTypeName.Should().Contain("DiscoveryOutput");
    }

    [Test]
    public void DiscoverTrains_TypedOutputTrainWithGraphQL_HasBothOutputTypeAndGraphQLEnabled()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var typed = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(GraphQLTypedOutputTrain)
        );

        typed.Should().NotBeNull();
        typed!.OutputType.Should().Be(typeof(DiscoveryOutput));
        typed!.IsMutation.Should().BeTrue();
    }

    [Test]
    public void DiscoverTrains_TypedOutputTrain_InputTypeIsAlsoCorrect()
    {
        var services = CreateServicesWithTrains();
        using var provider = services.BuildServiceProvider();

        var discovery = provider.GetRequiredService<ITrainDiscoveryService>();
        var registrations = discovery.DiscoverTrains();

        var typed = registrations.FirstOrDefault(r =>
            r.ImplementationType == typeof(TypedOutputTrain)
        );

        typed.Should().NotBeNull();
        typed!.InputType.Should().Be(typeof(TypedOutputInput));
    }

    #endregion

    #region Setup

    private static IServiceCollection CreateServicesWithTrains()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects)
                .AddMediator(assemblies: [typeof(TrainDiscoveryServiceTests).Assembly])
        );
        return services;
    }

    #endregion

    #region Test Trains

    public record PlainInput;

    public record SubscriptionInput;

    public record GraphQLOnlyInput;

    public record BothInput;

    public interface IPlainTrain : IServiceTrain<PlainInput, Unit>;

    public class PlainTrain : ServiceTrain<PlainInput, Unit>, IPlainTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(PlainInput input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface ISubscriptionTrain : IServiceTrain<SubscriptionInput, Unit>;

    [TraxBroadcast]
    public class SubscriptionTrain : ServiceTrain<SubscriptionInput, Unit>, ISubscriptionTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(SubscriptionInput input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IGraphQLOnlyTrain : IServiceTrain<GraphQLOnlyInput, Unit>;

    [TraxMutation(Description = "GraphQL only")]
    public class GraphQLOnlyTrain : ServiceTrain<GraphQLOnlyInput, Unit>, IGraphQLOnlyTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(GraphQLOnlyInput input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IBothTrain : IServiceTrain<BothInput, Unit>;

    [TraxMutation(Description = "Both")]
    [TraxBroadcast]
    public class GraphQLAndSubscriptionTrain : ServiceTrain<BothInput, Unit>, IBothTrain
    {
        protected override Task<Either<Exception, Unit>> RunInternal(BothInput input) =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public record TypedOutputInput;

    public record DiscoveryOutput
    {
        public string Result { get; init; } = "";
    }

    public record GraphQLTypedOutputInput;

    public interface ITypedOutputTrain : IServiceTrain<TypedOutputInput, DiscoveryOutput>;

    public class TypedOutputTrain
        : ServiceTrain<TypedOutputInput, DiscoveryOutput>,
            ITypedOutputTrain
    {
        protected override Task<Either<Exception, DiscoveryOutput>> RunInternal(
            TypedOutputInput input
        ) =>
            Task.FromResult<Either<Exception, DiscoveryOutput>>(
                new DiscoveryOutput { Result = "ok" }
            );
    }

    public interface IGraphQLTypedOutputTrain
        : IServiceTrain<GraphQLTypedOutputInput, DiscoveryOutput>;

    [TraxMutation(Description = "Typed output with GraphQL")]
    public class GraphQLTypedOutputTrain
        : ServiceTrain<GraphQLTypedOutputInput, DiscoveryOutput>,
            IGraphQLTypedOutputTrain
    {
        protected override Task<Either<Exception, DiscoveryOutput>> RunInternal(
            GraphQLTypedOutputInput input
        ) =>
            Task.FromResult<Either<Exception, DiscoveryOutput>>(
                new DiscoveryOutput { Result = "graphql" }
            );
    }

    #endregion
}
