using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class CoverageGapTests
{
    [SetUp]
    public void Setup() => TrainBus.ClearMethodCache();

    [TearDown]
    public void TearDown() => TrainBus.ClearMethodCache();

    #region NullPrincipalProvider

    [Test]
    public void NullPrincipalProvider_ReturnsNull()
    {
        var provider = new NullPrincipalProvider();
        provider.GetCurrentPrincipalId().Should().BeNull();
    }

    #endregion

    #region Internal RegisterServiceTrains overload (lifetime switch)

    [Test]
    public void RegisterServiceTrains_Internal_Singleton_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterServiceTrains(
            new[] { (typeof(IGapTrain), typeof(GapTrain)) },
            ServiceLifetime.Singleton
        );

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IGapTrain) && d.Lifetime == ServiceLifetime.Singleton
            );
    }

    [Test]
    public void RegisterServiceTrains_Internal_Scoped_RegistersAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterServiceTrains(
            new[] { (typeof(IGapTrain), typeof(GapTrain)) },
            ServiceLifetime.Scoped
        );

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IGapTrain) && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void RegisterServiceTrains_Internal_InvalidLifetime_Throws()
    {
        var services = new ServiceCollection();

        Action act = () =>
            services.RegisterServiceTrains(
                new[] { (typeof(IGapTrain), typeof(GapTrain)) },
                (ServiceLifetime)999
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region TrainExecutionService.QueueAsync

    private static IServiceProvider BuildProviderWithGapTrains(bool authorized = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator => mediator.ScanAssemblies(typeof(CoverageGapTests).Assembly))
        );
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task QueueAsync_HappyPath_PersistsWorkQueueEntry()
    {
        using var provider = (ServiceProvider)BuildProviderWithGapTrains();
        var execution = provider.GetRequiredService<ITrainExecutionService>();
        var inputJson = JsonSerializer.Serialize(
            new GapInput { Value = "queued" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var result = await execution.QueueAsync(nameof(IGapTrain), inputJson, priority: 5);

        result.WorkQueueId.Should().BeGreaterThan(0);
        result.ExternalId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task QueueAsync_UnknownTrain_ThrowsInvalidOperation()
    {
        using var provider = (ServiceProvider)BuildProviderWithGapTrains();
        var execution = provider.GetRequiredService<ITrainExecutionService>();

        var act = async () => await execution.QueueAsync("Does.Not.Exist", "{}");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task QueueAsync_OversizedJson_ThrowsTrainInputValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator =>
                    mediator
                        .ScanAssemblies(typeof(CoverageGapTests).Assembly)
                        .WithMaxInputJsonBytes(64)
                )
        );
        using var provider = services.BuildServiceProvider();
        var execution = provider.GetRequiredService<ITrainExecutionService>();

        var oversized = new GapInput { Value = new string('x', 1024) };
        var inputJson = JsonSerializer.Serialize(
            oversized,
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var act = async () => await execution.QueueAsync(nameof(IGapTrain), inputJson);

        await act.Should().ThrowAsync<Mediator.Exceptions.TrainInputValidationException>();
    }

    [Test]
    public async Task RunAsync_DeserializeReturnsNull_ThrowsInvalidOperation()
    {
        using var provider = (ServiceProvider)BuildProviderWithGapTrains();
        var execution = provider.GetRequiredService<ITrainExecutionService>();

        // The literal "null" deserializes to null for reference types — covers the
        // null-check after JsonSerializer.Deserialize in DeserializeInput.
        var act = async () => await execution.RunAsync(nameof(IGapTrain), "null");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RunAsync_AuthorizedTrain_NoAuthService_ThrowsInvalidOperation()
    {
        // A train carrying [TraxAuthorize] requires ITrainAuthorizationService unless
        // the host opted in to AllowMissingAuthorizationService. We deliberately don't
        // opt in, so execution must fail closed.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator => mediator.ScanAssemblies(typeof(CoverageGapTests).Assembly))
        );
        using var provider = services.BuildServiceProvider();
        var execution = provider.GetRequiredService<ITrainExecutionService>();

        var inputJson = JsonSerializer.Serialize(
            new AuthGapInput { Value = "x" },
            TraxEffectConfiguration.StaticSystemJsonSerializerOptions
        );

        var trainName = typeof(IAuthorizedGapTrain).FullName!;
        var act = async () => await execution.RunAsync(trainName, inputJson);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ITrainAuthorizationService*");
    }

    #endregion

    #region TrainDiscoveryService — GraphQL attributes

    [Test]
    public void Discover_TraxQueryAttribute_PopulatesGraphQLMetadata()
    {
        var services = new ServiceCollection();
        services.AddScoped<IQueryGapTrain, QueryGapTrain>();
        services.AddScoped<QueryGapTrain>();

        var discovery = new TrainDiscoveryService(services);
        var reg = discovery.DiscoverTrains().Single(r => r.ServiceType == typeof(IQueryGapTrain));

        reg.IsQuery.Should().BeTrue();
        reg.IsMutation.Should().BeFalse();
        reg.GraphQLName.Should().Be("customQueryName");
    }

    [Test]
    public void Discover_TraxMutationAttribute_PopulatesGraphQLMetadata()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMutationGapTrain, MutationGapTrain>();
        services.AddScoped<MutationGapTrain>();

        var discovery = new TrainDiscoveryService(services);
        var reg = discovery
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(IMutationGapTrain));

        reg.IsMutation.Should().BeTrue();
        reg.IsQuery.Should().BeFalse();
    }

    #endregion

    #region Test trains

    public record GapInput
    {
        public string Value { get; init; } = "";
    }

    public record GapOutput
    {
        public string Echo { get; init; } = "";
    }

    public interface IGapTrain : IServiceTrain<GapInput, GapOutput>;

    public class GapTrain : ServiceTrain<GapInput, GapOutput>, IGapTrain
    {
        protected override Task<Either<Exception, GapOutput>> RunInternal(GapInput input) =>
            Task.FromResult<Either<Exception, GapOutput>>(new GapOutput { Echo = input.Value });
    }

    public record AuthGapInput
    {
        public string Value { get; init; } = "";
    }

    [TraxAuthorize("Admin")]
    public interface IAuthorizedGapTrain : IServiceTrain<AuthGapInput, GapOutput>;

    public class AuthorizedGapTrain : ServiceTrain<AuthGapInput, GapOutput>, IAuthorizedGapTrain
    {
        protected override Task<Either<Exception, GapOutput>> RunInternal(AuthGapInput input) =>
            Task.FromResult<Either<Exception, GapOutput>>(new GapOutput { Echo = input.Value });
    }

    public record QueryGapInput
    {
        public string Value { get; init; } = "";
    }

    public interface IQueryGapTrain : IServiceTrain<QueryGapInput, GapOutput>;

    [TraxQuery(Name = "customQueryName")]
    public class QueryGapTrain : ServiceTrain<QueryGapInput, GapOutput>, IQueryGapTrain
    {
        protected override Task<Either<Exception, GapOutput>> RunInternal(QueryGapInput input) =>
            Task.FromResult<Either<Exception, GapOutput>>(new GapOutput { Echo = input.Value });
    }

    public record MutationGapInput
    {
        public string Value { get; init; } = "";
    }

    public interface IMutationGapTrain : IServiceTrain<MutationGapInput, GapOutput>;

    [TraxMutation]
    public class MutationGapTrain : ServiceTrain<MutationGapInput, GapOutput>, IMutationGapTrain
    {
        protected override Task<Either<Exception, GapOutput>> RunInternal(MutationGapInput input) =>
            Task.FromResult<Either<Exception, GapOutput>>(new GapOutput { Echo = input.Value });
    }

    #endregion
}
