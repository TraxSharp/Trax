using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// Behavior of <c>ExposeOperationQueries</c> and <c>ExposeOperationMutations</c>:
/// the <c>operations</c> namespace is opt-in, and the predefined dead-letter
/// surface is reachable only as a nested namespace under <c>operations</c>.
/// Each test wires its own minimal DI graph and inspects the resulting schema.
/// </summary>
[TestFixture]
public class OperationsExposureTests
{
    private ITrainDiscoveryService _emptyDiscovery = null!;
    private ITrainDiscoveryService _queryOnlyDiscovery = null!;
    private ITrainDiscoveryService _mutationOnlyDiscovery = null!;
    private ITrainDiscoveryService _queryAndMutationDiscovery = null!;
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void SetUp()
    {
        _emptyDiscovery = Substitute.For<ITrainDiscoveryService>();
        _emptyDiscovery.DiscoverTrains().Returns([]);

        var queryReg = new TrainRegistration
        {
            ServiceType = typeof(IFakeQueryTrain),
            ImplementationType = typeof(FakeQueryTrain),
            InputType = typeof(FakeInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IFakeQueryTrain),
            ImplementationTypeName = nameof(FakeQueryTrain),
            HasAllowAnonymousAttribute = true,
            InputTypeName = nameof(FakeInput),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = true,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

        _queryOnlyDiscovery = Substitute.For<ITrainDiscoveryService>();
        _queryOnlyDiscovery.DiscoverTrains().Returns([queryReg]);

        var mutationReg = new TrainRegistration
        {
            ServiceType = typeof(IFakeMutationTrain),
            ImplementationType = typeof(FakeMutationTrain),
            InputType = typeof(FakeInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IFakeMutationTrain),
            ImplementationTypeName = nameof(FakeMutationTrain),
            HasAllowAnonymousAttribute = true,
            InputTypeName = nameof(FakeInput),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = true,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

        _mutationOnlyDiscovery = Substitute.For<ITrainDiscoveryService>();
        _mutationOnlyDiscovery.DiscoverTrains().Returns([mutationReg]);

        _queryAndMutationDiscovery = Substitute.For<ITrainDiscoveryService>();
        _queryAndMutationDiscovery.DiscoverTrains().Returns([queryReg, mutationReg]);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    #region Builder flags

    [Test]
    public void ExposeOperationQueries_DefaultIsFalse()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.OperationQueriesExposed.Should().BeFalse();
    }

    [Test]
    public void ExposeOperationMutations_DefaultIsFalse()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.OperationMutationsExposed.Should().BeFalse();
    }

    [Test]
    public void ExposeOperationQueries_FlipsFlag()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.ExposeOperationQueries();

        builder.OperationQueriesExposed.Should().BeTrue();
    }

    [Test]
    public void ExposeOperationMutations_FlipsFlag()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.ExposeOperationMutations();

        builder.OperationMutationsExposed.Should().BeTrue();
    }

    [Test]
    public void ExposeOperations_FluentChain()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var chained = builder.ExposeOperationQueries().ExposeOperationMutations();

        chained.Should().BeSameAs(builder);
        builder.OperationQueriesExposed.Should().BeTrue();
        builder.OperationMutationsExposed.Should().BeTrue();
    }

    [Test]
    public void GraphQLConfiguration_OperationFlags_DefaultFalse()
    {
        var config = new GraphQLConfiguration([], [], [], []);

        config.OperationQueriesExposed.Should().BeFalse();
        config.OperationMutationsExposed.Should().BeFalse();
    }

    #endregion

    #region Default behavior — operations namespace omitted

    [Test]
    public async Task Default_OperationsNamespace_OmittedFromQuerySchema()
    {
        var executor = await BuildExecutor(_queryOnlyDiscovery);

        var result = await executor.ExecuteAsync("{ operations { health { status } } }");

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().NotBeNullOrEmpty();
        operationResult.Errors!.Any(e => e.Message.Contains("operations")).Should().BeTrue();
    }

    [Test]
    public async Task Default_NoTrainMutations_RootMutationTypeAbsent()
    {
        var executor = await BuildExecutor(_queryOnlyDiscovery);

        var result = await executor.ExecuteAsync(
            "mutation { operations { triggerManifest(externalId: \"x\") { success } } }"
        );

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Default_NoQueriesAtAll_AddTraxGraphQL_ThrowsHelpfulError()
    {
        var services = BuildBaseServices(_emptyDiscovery);

        Action act = () =>
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(services);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ExposeOperationQueries*");
    }

    #endregion

    #region Opt-in queries

    [Test]
    public async Task ExposeOperationQueries_OperationsNamespaceQueryable()
    {
        _healthService = Substitute.For<ITraxHealthService>();
        _healthService
            .GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new Trax.Api.DTOs.HealthStatus("Healthy", "ok", 0, 0, 0, 0));

        var executor = await BuildExecutor(
            _emptyDiscovery,
            graphql => graphql.ExposeOperationQueries(),
            registerHealth: true
        );

        var result = await executor.ExecuteAsync("{ operations { health { status } } }");

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        operationResult.ToJson().Should().Contain("Healthy");
    }

    [Test]
    public async Task ExposeOperationQueries_DeadLettersNestedUnderOperations()
    {
        var executor = await BuildExecutor(
            _emptyDiscovery,
            graphql => graphql.ExposeOperationQueries(),
            registerHealth: true
        );

        // Validate the field exists (resolver hits the EF DbContext factory which we
        // do not register, so we expect a runtime error rather than a schema error).
        var schemaResult = await executor.ExecuteAsync(
            "{ __type(name: \"OperationsQueries\") { fields { name } } }"
        );
        var opResult = schemaResult as OperationResult;
        opResult!.Errors.Should().BeNullOrEmpty();
        opResult.ToJson().Should().Contain("deadLetters");
    }

    [Test]
    public async Task ExposeOperationQueries_DeadLettersNotAtRoot()
    {
        var executor = await BuildExecutor(
            _emptyDiscovery,
            graphql => graphql.ExposeOperationQueries(),
            registerHealth: true
        );

        var result = await executor.ExecuteAsync(
            "{ __type(name: \"RootQuery\") { fields { name } } }"
        );
        var opResult = result as OperationResult;
        opResult!.Errors.Should().BeNullOrEmpty();
        var json = opResult.ToJson();
        json.Should().Contain("operations");
        json.Should().NotContain("deadLetters");
    }

    #endregion

    #region Opt-in mutations

    [Test]
    public async Task ExposeOperationMutations_OperationsNamespaceQueryable()
    {
        _scheduler = Substitute.For<ITraxScheduler>();

        var executor = await BuildExecutor(
            _queryOnlyDiscovery,
            graphql => graphql.ExposeOperationMutations().AllowAnonymousOperations(),
            registerScheduler: true
        );

        var result = await executor.ExecuteAsync(
            "mutation { operations { triggerManifest(externalId: \"abc\") { success } } }"
        );

        var operationResult = result as OperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
        await _scheduler!.Received(1).TriggerAsync("abc", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExposeOperationMutations_DeadLettersNestedUnderOperations()
    {
        _scheduler = Substitute.For<ITraxScheduler>();

        var executor = await BuildExecutor(
            _queryOnlyDiscovery,
            graphql => graphql.ExposeOperationMutations().AllowAnonymousOperations(),
            registerScheduler: true
        );

        var schemaResult = await executor.ExecuteAsync(
            "{ __type(name: \"OperationsMutations\") { fields { name } } }"
        );
        var opResult = schemaResult as OperationResult;
        opResult!.Errors.Should().BeNullOrEmpty();
        opResult.ToJson().Should().Contain("deadLetters");
    }

    [Test]
    public async Task ExposeOperationMutations_AddsMutationRoot_EvenWithoutTrainMutations()
    {
        var executor = await BuildExecutor(
            _queryOnlyDiscovery,
            graphql => graphql.ExposeOperationMutations().AllowAnonymousOperations(),
            registerScheduler: true
        );

        var result = await executor.ExecuteAsync("{ __schema { mutationType { name } } }");
        var opResult = result as OperationResult;
        opResult!.Errors.Should().BeNullOrEmpty();
        opResult.ToJson().Should().Contain("RootMutation");
    }

    [Test]
    public async Task QueriesExposed_MutationsNotExposed_NoMutationRoot()
    {
        var executor = await BuildExecutor(
            _queryOnlyDiscovery,
            graphql => graphql.ExposeOperationQueries(),
            registerHealth: true
        );

        var result = await executor.ExecuteAsync("{ __schema { mutationType { name } } }");
        var opResult = result as OperationResult;
        opResult!.Errors.Should().BeNullOrEmpty();
        // mutationType is null when the schema has no mutation root.
        opResult.ToJson().Replace(" ", "").Should().Contain("\"mutationType\":null");
    }

    [Test]
    public async Task TrainMutationsExist_MutationRootRegistered_WithoutOpsFlag()
    {
        // Both query and mutation trains present so the schema build is valid;
        // we are asserting that the mutation root appears purely from the train
        // mutation, not from any operations flag.
        var executor = await BuildExecutor(_queryAndMutationDiscovery);

        var result = await executor.ExecuteAsync("{ __schema { mutationType { name } } }");
        var opResult = result as OperationResult;
        opResult!.Errors.Should().BeNullOrEmpty();
        opResult.ToJson().Should().Contain("RootMutation");
    }

    #endregion

    #region Operations authorization guard

    [Test]
    public void AllowAnonymousOperations_DefaultIsFalse()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AnonymousOperationsAllowed.Should().BeFalse();
    }

    [Test]
    public void AllowAnonymousOperations_FlipsFlag()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AllowAnonymousOperations();

        builder.AnonymousOperationsAllowed.Should().BeTrue();
    }

    [Test]
    public void ExposeOperationMutations_WithoutAuthorization_ThrowsAtBuild()
    {
        var services = BuildBaseServices(_emptyDiscovery);

        Action act = () =>
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                services,
                g => g.ExposeOperationQueries().ExposeOperationMutations()
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*RequireAuthorization*")
            .WithMessage("*AllowAnonymousOperations*");
    }

    [Test]
    public void ExposeOperationMutations_WithRequireAuthorization_DoesNotThrow()
    {
        var services = BuildBaseServices(_emptyDiscovery);

        Action act = () =>
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                services,
                g => g.ExposeOperationQueries().ExposeOperationMutations().RequireAuthorization()
            );

        act.Should().NotThrow();
    }

    [Test]
    public void ExposeOperationMutations_WithAllowAnonymousOperations_DoesNotThrow()
    {
        var services = BuildBaseServices(_emptyDiscovery);

        Action act = () =>
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                services,
                g =>
                    g.ExposeOperationQueries().ExposeOperationMutations().AllowAnonymousOperations()
            );

        act.Should().NotThrow();
    }

    [Test]
    public void ExposeOperationQueriesOnly_WithoutAuthorization_DoesNotThrow()
    {
        // The guard is about scheduler-control mutations. Read-only operation queries stay reachable
        // without a gate (health checks and dashboards commonly want that).
        var services = BuildBaseServices(_emptyDiscovery);

        Action act = () =>
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                services,
                g => g.ExposeOperationQueries()
            );

        act.Should().NotThrow();
    }

    [Test]
    public void RequireAuthorization_WithAllowAnonymousOperations_ThrowsContradiction()
    {
        var services = BuildBaseServices(_emptyDiscovery);

        Action act = () =>
            Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                services,
                g => g.ExposeOperationQueries().RequireAuthorization().AllowAnonymousOperations()
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*contradict*");
    }

    #endregion

    #region Helpers

    private ITraxHealthService? _healthService;
    private ITraxScheduler? _scheduler;

    private static IServiceCollection BuildBaseServices(ITrainDiscoveryService discovery)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton<ITrainDiscoveryService>(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        return services;
    }

    private async Task<IRequestExecutor> BuildExecutor(
        ITrainDiscoveryService discovery,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? configure = null,
        bool registerHealth = false,
        bool registerScheduler = false
    )
    {
        var services = BuildBaseServices(discovery);

        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            configure ?? (b => b)
        );

        if (registerHealth)
            services.AddScoped(_ => _healthService ?? Substitute.For<ITraxHealthService>());
        if (registerScheduler)
            services.AddScoped(_ => _scheduler ?? Substitute.For<ITraxScheduler>());

        // Always register stubs in case the schema asks for them via type wiring.
        services.AddScoped<ITraxHealthService>(_ =>
            _healthService ?? Substitute.For<ITraxHealthService>()
        );
        services.AddScoped<ITraxScheduler>(_ => _scheduler ?? Substitute.For<ITraxScheduler>());

        _serviceProvider = services.BuildServiceProvider();

        return await _serviceProvider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    #endregion

    #region Test types

    private interface IFakeQueryTrain;

    private class FakeQueryTrain;

    private interface IFakeMutationTrain;

    private class FakeMutationTrain;

    public record FakeInput
    {
        public string Value { get; init; } = "";
    }

    #endregion
}
