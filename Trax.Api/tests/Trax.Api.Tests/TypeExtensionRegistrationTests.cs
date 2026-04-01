using System.Reflection;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

[TestFixture]
public class TypeExtensionRegistrationTests
{
    #region AddTypeExtension — Builder Storage

    [Test]
    public void AddTypeExtension_SingleType_StoredInBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtension<StubTypeExtensionA>();

        builder
            .AdditionalTypeExtensions.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(StubTypeExtensionA));
    }

    [Test]
    public void AddTypeExtension_MultipleCalls_AllStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtension<StubTypeExtensionA>();
        builder.AddTypeExtension<StubTypeExtensionB>();

        builder.AdditionalTypeExtensions.Should().HaveCount(2);
        builder
            .AdditionalTypeExtensions.Should()
            .ContainInOrder(typeof(StubTypeExtensionA), typeof(StubTypeExtensionB));
    }

    [Test]
    public void AddTypeExtension_ReturnsSameBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var result = builder.AddTypeExtension<StubTypeExtensionA>();

        result.Should().BeSameAs(builder);
    }

    [Test]
    public void AddTypeExtension_FluentChaining_AllTypesStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtension<StubTypeExtensionA>().AddTypeExtension<StubTypeExtensionB>();

        builder.AdditionalTypeExtensions.Should().HaveCount(2);
    }

    [Test]
    public void AddTypeExtension_SameTypeTwice_BothStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtension<StubTypeExtensionA>();
        builder.AddTypeExtension<StubTypeExtensionA>();

        builder.AdditionalTypeExtensions.Should().HaveCount(2);
    }

    #endregion

    #region AddTypeExtensions — Assembly Scanning

    [Test]
    public void AddTypeExtensions_FindsDecoratedClasses()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtensions(typeof(StubTypeExtensionA).Assembly);

        builder.AdditionalTypeExtensions.Should().Contain(typeof(StubTypeExtensionA));
        builder.AdditionalTypeExtensions.Should().Contain(typeof(StubTypeExtensionB));
    }

    [Test]
    public void AddTypeExtensions_IgnoresAbstractClasses()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtensions(typeof(AbstractTypeExtension).Assembly);

        builder.AdditionalTypeExtensions.Should().NotContain(typeof(AbstractTypeExtension));
    }

    [Test]
    public void AddTypeExtensions_IgnoresNonDecoratedClasses()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtensions(typeof(NotATypeExtension).Assembly);

        builder.AdditionalTypeExtensions.Should().NotContain(typeof(NotATypeExtension));
    }

    [Test]
    public void AddTypeExtensions_SameAssemblyTwice_DuplicatesIncluded()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        var assembly = typeof(StubTypeExtensionA).Assembly;

        builder.AddTypeExtensions(assembly, assembly);

        var countA = builder.AdditionalTypeExtensions.Count(t => t == typeof(StubTypeExtensionA));
        countA.Should().Be(2);
    }

    [Test]
    public void AddTypeExtensions_ReturnsSameBuilder()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var result = builder.AddTypeExtensions(typeof(StubTypeExtensionA).Assembly);

        result.Should().BeSameAs(builder);
    }

    [Test]
    public void AddTypeExtensions_CombinedWithExplicitAddTypeExtension_AllStored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.AddTypeExtension<StubTypeExtensionA>();
        builder.AddTypeExtensions(typeof(StubTypeExtensionB).Assembly);

        builder.AdditionalTypeExtensions.Should().Contain(typeof(StubTypeExtensionA));
        builder.AdditionalTypeExtensions.Should().Contain(typeof(StubTypeExtensionB));
    }

    #endregion

    #region Build — Propagation to GraphQLConfiguration

    [Test]
    public void Build_WithTypeExtensions_PropagatedToConfig()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddTypeExtension<StubTypeExtensionA>();
        builder.AddTypeExtension<StubTypeExtensionB>();

        var config = builder.Build();

        config.AdditionalTypeExtensions.Should().HaveCount(2);
        config
            .AdditionalTypeExtensions.Should()
            .ContainInOrder(typeof(StubTypeExtensionA), typeof(StubTypeExtensionB));
    }

    [Test]
    public void Build_NoTypeExtensions_EmptyList()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        var config = builder.Build();

        config.AdditionalTypeExtensions.Should().BeEmpty();
    }

    [Test]
    public void Build_TypeExtensionsAndDbContext_BothPreserved()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddDbContext<TestDbContext>();
        builder.AddTypeExtension<StubTypeExtensionA>();

        var config = builder.Build();

        config.ModelRegistrations.Should().HaveCount(1);
        config.AdditionalTypeExtensions.Should().HaveCount(1);
    }

    [Test]
    public void Build_TypeExtensionsAndTypeModules_BothPreserved()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());
        builder.AddTypeModule<StubTypeModuleForExtTest>();
        builder.AddTypeExtension<StubTypeExtensionA>();

        var config = builder.Build();

        config.AdditionalTypeModules.Should().HaveCount(1);
        config.AdditionalTypeExtensions.Should().HaveCount(1);
    }

    #endregion

    #region GraphQLConfiguration — AdditionalTypeExtensions Property

    [Test]
    public void GraphQLConfiguration_WithTypeExtensions_ExposesReadOnlyList()
    {
        var types = new List<Type> { typeof(StubTypeExtensionA), typeof(StubTypeExtensionB) };
        var config = new GraphQLConfiguration([], [], [], types);

        config.AdditionalTypeExtensions.Should().HaveCount(2);
        config.AdditionalTypeExtensions[0].Should().Be(typeof(StubTypeExtensionA));
        config.AdditionalTypeExtensions[1].Should().Be(typeof(StubTypeExtensionB));
    }

    [Test]
    public void GraphQLConfiguration_EmptyTypeExtensions_EmptyList()
    {
        var config = new GraphQLConfiguration([], [], [], []);

        config.AdditionalTypeExtensions.Should().BeEmpty();
    }

    #endregion

    #region E2E — Type Extension Registered in Schema

    [Test]
    public async Task AddTraxGraphQL_WithTypeExtension_FieldQueryable()
    {
        // Arrange — build a real HotChocolate executor with a type extension
        // that adds a "ping" field to the operations query type.
        var discoveryService = Substitute.For<ITrainDiscoveryService>();
        discoveryService
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IFakeTrainForExtTest),
                    ImplementationType = typeof(FakeTrainForExtTest),
                    InputType = typeof(FakeExtTestInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IFakeTrainForExtTest),
                    ImplementationTypeName = nameof(FakeTrainForExtTest),
                    InputTypeName = nameof(FakeExtTestInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = true,
                    IsMutation = false,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
            ]);

        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discoveryService);
        services.AddSingleton(Substitute.For<IEffectRegistry>());

        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.AddTypeExtension<PingTypeExtension>()
        );

        services.AddScoped(_ => Substitute.For<Trax.Api.Services.HealthCheck.ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());

        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync("trax");

        // Act — query the field added by PingTypeExtension
        var result = await executor.ExecuteAsync("{ operations { ping } }");

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();

        var data = operationResult.Data;
        data.Should().NotBeNull();
    }

    [Test]
    public async Task AddTraxGraphQL_WithAssemblyScannedTypeExtension_FieldQueryable()
    {
        // Arrange — same as above but using assembly scanning
        var discoveryService = Substitute.For<ITrainDiscoveryService>();
        discoveryService
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IFakeTrainForExtTest),
                    ImplementationType = typeof(FakeTrainForExtTest),
                    InputType = typeof(FakeExtTestInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IFakeTrainForExtTest),
                    ImplementationTypeName = nameof(FakeTrainForExtTest),
                    InputTypeName = nameof(FakeExtTestInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = true,
                    IsMutation = false,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
            ]);

        var services = new ServiceCollection();
        services.AddSingleton<Trax.Effect.Configuration.TraxBuilder.TraxMarker>();
        services.AddSingleton(discoveryService);
        services.AddSingleton(Substitute.For<IEffectRegistry>());

        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            graphql => graphql.AddTypeExtensions(typeof(PingTypeExtension).Assembly)
        );

        services.AddScoped(_ => Substitute.For<Trax.Api.Services.HealthCheck.ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());

        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorResolver>()
            .GetRequestExecutorAsync("trax");

        // Act — PingTypeExtension should have been discovered via assembly scan
        var result = await executor.ExecuteAsync("{ operations { ping } }");

        // Assert
        var operationResult = result as IOperationResult;
        operationResult.Should().NotBeNull();
        operationResult!.Errors.Should().BeNullOrEmpty();
    }

    #endregion

    #region Stubs

    [ExtendObjectType(typeof(object))]
    public class StubTypeExtensionA;

    [ExtendObjectType(typeof(object))]
    public class StubTypeExtensionB;

    public class NotATypeExtension;

    [ExtendObjectType(typeof(object))]
    public abstract class AbstractTypeExtension;

    private class StubTypeModuleForExtTest : HotChocolate.Execution.Configuration.TypeModule
    {
        public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
            HotChocolate.Types.Descriptors.IDescriptorContext context,
            CancellationToken cancellationToken
        ) => new(Array.Empty<ITypeSystemMember>());
    }

    // E2E test types

    [ExtendObjectType(typeof(Trax.Api.GraphQL.Queries.OperationsQueries))]
    public class PingTypeExtension
    {
        public string Ping() => "pong";
    }

    private interface IFakeTrainForExtTest;

    private class FakeTrainForExtTest;

    private record FakeExtTestInput;

    #endregion
}
