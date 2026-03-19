using FluentAssertions;
using HotChocolate.Types;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// Tests for TrainTypeModule's CreateTypesAsync — verifies type generation
/// for typed vs Unit output trains, input/output deduplication, name derivation,
/// ExecutionMode enum registration, and per-train response types.
/// </summary>
[TestFixture]
public class TrainTypeModuleTests
{
    #region HasTypedOutput — ObjectType<TOut> registration

    [Test]
    public async Task CreateTypesAsync_UnitOutputTrain_DoesNotRegisterOutputObjectType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>("UnitTrain", typeof(Unit)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_ObjectOutputTrain_DoesNotRegisterOutputObjectType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>("ObjectTrain", typeof(object)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_TypedOutputTrain_RegistersOutputObjectType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>("TypedTrain", typeof(TypedOutput)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var outputTypes = GetGenericObjectTypes(types);
        outputTypes.Should().HaveCount(1);
        outputTypes[0].GetType().GetGenericArguments()[0].Should().Be(typeof(TypedOutput));
    }

    #endregion

    #region Input Type Deduplication

    [Test]
    public async Task CreateTypesAsync_TwoTrainsWithSameInputType_RegistersInputTypeOnce()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<SharedInput>("TrainA", typeof(Unit), serviceTypeName: "TrainA"),
            CreateRegistration<SharedInput>("TrainB", typeof(Unit), serviceTypeName: "TrainB"),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var inputTypes = types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(SharedInput)
            )
            .ToList();

        inputTypes.Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_TwoTrainsWithSameOutputType_RegistersOutputTypeOnce()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "TrainA",
                typeof(TypedOutput),
                serviceTypeName: "TrainA"
            ),
            CreateRegistration<TypedInput2>(
                "TrainB",
                typeof(TypedOutput),
                serviceTypeName: "TrainB"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var outputTypes = types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(TypedOutput)
            )
            .ToList();

        outputTypes.Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_TwoTrainsWithDifferentOutputTypes_RegistersBoth()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "TrainA",
                typeof(TypedOutput),
                serviceTypeName: "TrainA"
            ),
            CreateRegistration<TypedInput2>(
                "TrainB",
                typeof(TypedOutput2),
                serviceTypeName: "TrainB"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var outputTypes = GetGenericObjectTypes(types);
        outputTypes.Should().HaveCount(2);
    }

    #endregion

    #region Field Generation and ObjectTypeExtension

    [Test]
    public async Task CreateTypesAsync_RunOnlyTrain_GeneratesExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "RunOnly",
                typeof(Unit),
                name: "RunOnly",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // DispatchMutations field extension + RootMutation dispatch extension
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_QueueOnlyTrain_GeneratesExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "QueueOnly",
                typeof(Unit),
                name: "QueueOnly",
                operations: GraphQLOperation.Queue
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // DispatchMutations field extension + RootMutation dispatch extension
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_NoGraphQLEnabledTrains_NoTypesGenerated()
    {
        var reg = new TrainRegistration
        {
            ServiceType = typeof(IDisabled),
            ImplementationType = typeof(Disabled),
            InputType = typeof(UnitInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = "IDisabled",
            ImplementationTypeName = "Disabled",
            InputTypeName = nameof(UnitInput),
            OutputTypeName = "Unit",
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run | GraphQLOperation.Queue,
            IsRemote = false,
        };
        var discovery = new StubDiscoveryService([reg]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_EmptyRegistrations_ReturnsEmptyCollection()
    {
        var discovery = new StubDiscoveryService([]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_GraphQLEnabledTrains_GeneratesTwoExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "TrainA",
                typeof(TypedOutput),
                serviceTypeName: "TrainA"
            ),
            CreateRegistration<UnitInput>("TrainB", typeof(Unit), serviceTypeName: "TrainB"),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // DispatchMutations field extension + RootMutation dispatch extension
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    #endregion

    #region Type Counts — verifying typed vs Unit train type generation

    [Test]
    public async Task CreateTypesAsync_UnitTrain_GeneratesInputResponseEnumAndExtensions()
    {
        // Unit train with RunAndQueue →
        // InputObjectType<TIn> + ObjectType (response) + EnumType (ExecutionMode)
        // + ObjectType<DispatchMutations> + 2x ObjectTypeExtension (DispatchMutations fields + RootMutation dispatch) = 6
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>("UnitTrain", typeof(Unit)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(6);
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
        types.Should().ContainSingle(t => t is EnumType);
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            );
        // Per-train response type
        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_TypedTrain_GeneratesInputOutputResponseEnumAndExtensions()
    {
        // Typed train with RunAndQueue →
        // InputObjectType<TIn> + ObjectType<TOut> + ObjectType (response) + EnumType
        // + ObjectType<DispatchMutations> + 2x ObjectTypeExtension = 7
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>("TypedTrain", typeof(TypedOutput)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(7);
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
        types.Should().ContainSingle(t => t is EnumType);
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            );
        // ObjectType<TOut> (GetGenericObjectTypes excludes namespace marker types)
        GetGenericObjectTypes(types).Should().HaveCount(1);
        // Per-train response type (non-generic ObjectType)
        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_MixedTrains_CorrectTypeCount()
    {
        // 1 typed + 1 Unit, different input types, both RunAndQueue →
        // InputObjectType<TypedInput> + InputObjectType<UnitInput> + ObjectType<TypedOutput>
        // + 2x ObjectType (response) + EnumType
        // + ObjectType<DispatchMutations> + 2x ObjectTypeExtension (DispatchMutations fields + RootMutation) = 9
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "TypedTrain",
                typeof(TypedOutput),
                serviceTypeName: "TypedTrain"
            ),
            CreateRegistration<UnitInput>("UnitTrain", typeof(Unit), serviceTypeName: "UnitTrain"),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(9);
    }

    [Test]
    public async Task CreateTypesAsync_RunOnlyTrain_DoesNotRegisterExecutionModeEnum()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "RunOnly",
                typeof(Unit),
                name: "RunOnly",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.OfType<EnumType>().Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_QueueOnlyTrain_DoesNotRegisterExecutionModeEnum()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "QueueOnly",
                typeof(Unit),
                name: "QueueOnly",
                operations: GraphQLOperation.Queue
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.OfType<EnumType>().Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_RunAndQueueTrain_RegistersExecutionModeEnum()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "Both",
                typeof(Unit),
                name: "Both",
                operations: GraphQLOperation.Run | GraphQLOperation.Queue
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.OfType<EnumType>().Should().HaveCount(1);
    }

    #endregion

    #region Description & Deprecation — verify no exceptions

    [Test]
    public async Task CreateTypesAsync_TrainWithDescription_DoesNotThrow()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "Described",
                typeof(Unit),
                name: "Described",
                description: "Test description"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);
        types.Should().NotBeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_TrainWithDeprecationReason_DoesNotThrow()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "Deprecated",
                typeof(Unit),
                name: "Deprecated",
                deprecationReason: "Use newTrain instead"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);
        types.Should().NotBeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_TypedTrainWithDescriptionAndDeprecation_DoesNotThrow()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "FullTrain",
                typeof(TypedOutput),
                name: "Full",
                description: "A fully described train",
                deprecationReason: "v2 coming"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);
        types.Should().NotBeEmpty();
    }

    #endregion

    #region Name Collision

    [Test]
    public async Task CreateTypesAsync_DuplicateNames_FallsBackToFullyQualifiedName()
    {
        var reg1 = CreateRegistration<TypedInput>(
            "DupeTrain",
            typeof(TypedOutput),
            serviceTypeName: "DupeTrain"
        );
        var reg2 = CreateRegistration<TypedInput2>(
            "DupeTrain",
            typeof(TypedOutput2),
            serviceTypeName: "DupeTrain"
        );
        var discovery = new StubDiscoveryService([reg1, reg2]);
        var module = new TrainTypeModule(discovery);

        var act = async () => await module.CreateTypesAsync(null!, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Multiple Typed Output Trains

    [Test]
    public async Task CreateTypesAsync_MultipleTypedTrains_RegistersAllOutputTypes()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "AlphaTrain",
                typeof(TypedOutput),
                serviceTypeName: "AlphaTrain"
            ),
            CreateRegistration<TypedInput2>(
                "BetaTrain",
                typeof(TypedOutput2),
                serviceTypeName: "BetaTrain"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var outputTypes = GetGenericObjectTypes(types);
        outputTypes.Should().HaveCount(2);
        outputTypes
            .Select(t => t.GetType().GetGenericArguments()[0])
            .Should()
            .Contain([typeof(TypedOutput), typeof(TypedOutput2)]);
    }

    [Test]
    public async Task CreateTypesAsync_MultipleTypedTrains_EachGetsOwnResponseType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "AlphaTrain",
                typeof(TypedOutput),
                name: "Alpha",
                serviceTypeName: "AlphaTrain"
            ),
            CreateRegistration<TypedInput2>(
                "BetaTrain",
                typeof(TypedOutput2),
                name: "Beta",
                serviceTypeName: "BetaTrain"
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Each mutation train gets its own response ObjectType
        GetNonGenericObjectTypes(types).Should().HaveCount(2);
    }

    #endregion

    #region Per-Train Response Type Presence

    [Test]
    public async Task CreateTypesAsync_TypedOutputTrain_CreatesPerTrainResponseType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>("TypedTrain", typeof(TypedOutput), name: "Typed"),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_UnitOutputTrain_CreatesPerTrainResponseType()
    {
        // All mutation trains now get a response type (externalId + metadataId/workQueueId)
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>("UnitTrain", typeof(Unit), name: "Unit"),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_QueueOnlyTypedTrain_CreatesResponseType()
    {
        // Queue-only trains now get a per-train response type with externalId + workQueueId
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "QueueOnly",
                typeof(TypedOutput),
                name: "QueueOnly",
                operations: GraphQLOperation.Queue
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_RunAndQueueTypedTrain_CreatesOneResponseType()
    {
        // RunAndQueue typed train: single response type with all fields
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "Both",
                typeof(TypedOutput),
                name: "Both",
                operations: GraphQLOperation.Run | GraphQLOperation.Queue
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    #endregion

    #region Query Train Generation

    [Test]
    public async Task CreateTypesAsync_QueryTrain_GeneratesDiscoverQueriesExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // DiscoverQueries field extension + RootQuery discover extension
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_QueryTrainWithTypedOutput_NoPerTrainResponseType()
    {
        // Query trains return output directly, not wrapped in a response type
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_MixedQueryAndMutationTrains_GeneratesFourExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                serviceTypeName: "LookupTrain",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
            CreateRegistration<UnitInput>(
                "BanTrain",
                typeof(Unit),
                name: "Ban",
                serviceTypeName: "BanTrain",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // DiscoverQueries fields + RootQuery discover + DispatchMutations fields + RootMutation dispatch
        types.OfType<ObjectTypeExtension>().Should().HaveCount(4);
    }

    [Test]
    public async Task CreateTypesAsync_QueryTrainWithTypedOutput_TypeCount()
    {
        // Query typed train →
        // InputObjectType<TIn> + ObjectType<TOut>
        // + ObjectType<DiscoverQueries> + 2x ObjectTypeExtension (DiscoverQueries fields + RootQuery discover) = 5
        // No per-train response type or ExecutionMode enum for queries
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(5);
    }

    #endregion

    #region Conditional Root Type Extensions

    [Test]
    public async Task CreateTypesAsync_OnlyMutationTrains_ExtendsRootMutationNotRootQuery()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "BanTrain",
                typeof(Unit),
                name: "Ban",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should have ObjectType<DispatchMutations> but not ObjectType<DiscoverQueries>
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DispatchMutations)
            );
        types
            .Should()
            .NotContain(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DiscoverQueries)
            );
    }

    [Test]
    public async Task CreateTypesAsync_OnlyQueryTrains_ExtendsRootQueryNotRootMutation()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should have ObjectType<DiscoverQueries> but not ObjectType<DispatchMutations>
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DiscoverQueries)
            );
        types
            .Should()
            .NotContain(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DispatchMutations)
            );
    }

    [Test]
    public async Task CreateTypesAsync_MixedTrains_ExtendsBothRootTypes()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                serviceTypeName: "LookupTrain",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
            CreateRegistration<UnitInput>(
                "BanTrain",
                typeof(Unit),
                name: "Ban",
                serviceTypeName: "BanTrain",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DiscoverQueries)
            );
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && t.GetType().GetGenericArguments()[0] == typeof(DispatchMutations)
            );
    }

    [Test]
    public async Task CreateTypesAsync_EmptyRegistrations_NoNamespaceTypesRegistered()
    {
        var discovery = new StubDiscoveryService([]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().BeEmpty();
        types
            .Should()
            .NotContain(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && NamespaceMarkerTypes.Contains(t.GetType().GetGenericArguments()[0])
            );
    }

    #endregion

    #region Namespace Grouping

    [Test]
    public async Task CreateTypesAsync_MutationWithNamespace_CreatesNamespaceTypeAndExtension()
    {
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>(
                "BanTrain",
                typeof(Unit),
                name: "Ban",
                operations: GraphQLOperation.Run,
                graphqlNamespace: "players"
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should have a non-generic ObjectType for the namespace (PlayersDispatchMutations)
        var nsTypes = types.Where(t => t.GetType() == typeof(ObjectType)).ToList();
        // Per-train response type + namespace base type = 2
        nsTypes.Should().HaveCount(2);

        // Should have extensions: DispatchMutations (namespace field) + RootMutation (dispatch)
        // + PlayersDispatchMutations (fields)
        types.OfType<ObjectTypeExtension>().Should().HaveCount(3);
    }

    [Test]
    public async Task CreateTypesAsync_QueryWithNamespace_CreatesNamespaceTypeAndExtension()
    {
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "players"
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should have ObjectType<DiscoverQueries> + ObjectType for namespace (PlayersDiscoverQueries)
        // + InputObjectType<TypedInput> + ObjectType<TypedOutput>
        // + extensions: RootQuery (discover) + DiscoverQueries (namespace field) + PlayersDiscoverQueries (fields)
        types.OfType<ObjectTypeExtension>().Should().HaveCount(3);

        // Namespace base type should be registered
        config.RegisteredNamespaceTypes.Should().Contain("PlayersDiscoverQueries");
    }

    [Test]
    public async Task CreateTypesAsync_MultipleTrainsInSameNamespace_ShareIntermediateType()
    {
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                serviceTypeName: "LookupTrain",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "players"
            ),
            CreateRegistration<TypedInput2>(
                "SearchTrain",
                typeof(TypedOutput2),
                name: "Search",
                serviceTypeName: "SearchTrain",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "players"
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Only one namespace base type should be registered
        config.RegisteredNamespaceTypes.Count(n => n == "PlayersDiscoverQueries").Should().Be(1);

        // Only one namespace field extension on DiscoverQueries
        config.RegisteredNamespaceTypes.Should().Contain("DiscoverQueries.players");
    }

    [Test]
    public async Task CreateTypesAsync_DifferentNamespaces_CreatesSeparateTypes()
    {
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                serviceTypeName: "LookupTrain",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "players"
            ),
            CreateRegistration<TypedInput2>(
                "AlertTrain",
                typeof(TypedOutput2),
                name: "Alert",
                serviceTypeName: "AlertTrain",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "alerts"
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        config.RegisteredNamespaceTypes.Should().Contain("PlayersDiscoverQueries");
        config.RegisteredNamespaceTypes.Should().Contain("AlertsDiscoverQueries");
    }

    [Test]
    public async Task CreateTypesAsync_MixedNamespacedAndRoot_BothGenerated()
    {
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                serviceTypeName: "LookupTrain",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "players"
            ),
            CreateRegistration<TypedInput2>(
                "HealthTrain",
                typeof(TypedOutput2),
                name: "Health",
                serviceTypeName: "HealthTrain",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should have extensions for:
        // - RootQuery (discover)
        // - DiscoverQueries (namespace field for "players" + root field "Health")
        // - PlayersDiscoverQueries (fields)
        types.OfType<ObjectTypeExtension>().Should().HaveCountGreaterThanOrEqualTo(3);

        // Namespace type should exist
        config.RegisteredNamespaceTypes.Should().Contain("PlayersDiscoverQueries");
    }

    [Test]
    public async Task CreateTypesAsync_NullNamespace_BackwardCompatible()
    {
        // Existing behavior: no namespace, fields go directly on DiscoverQueries
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // No namespace types should be registered
        config.RegisteredNamespaceTypes.Should().BeEmpty();

        // Should still have DiscoverQueries extension + RootQuery extension
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_SameNamespaceOnQueryAndMutation_CreatesSeparateNamespaceTypes()
    {
        var config = new GraphQLConfiguration([]);
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(TypedOutput),
                name: "Lookup",
                serviceTypeName: "LookupTrain",
                isQuery: true,
                operations: GraphQLOperation.Run,
                graphqlNamespace: "alerts"
            ),
            CreateRegistration<UnitInput>(
                "CreateAlertTrain",
                typeof(Unit),
                name: "CreateAlert",
                serviceTypeName: "CreateAlertTrain",
                operations: GraphQLOperation.Run,
                graphqlNamespace: "alerts"
            ),
        ]);
        var module = new TrainTypeModule(discovery, config);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Should create separate namespace types for queries vs mutations
        config.RegisteredNamespaceTypes.Should().Contain("AlertsDiscoverQueries");
        config.RegisteredNamespaceTypes.Should().Contain("AlertsDispatchMutations");
    }

    [Test]
    public void NamespaceTypeName_CombinesCorrectly()
    {
        TrainTypeModule
            .NamespaceTypeName("alerts", "DiscoverQueries")
            .Should()
            .Be("AlertsDiscoverQueries");
        TrainTypeModule
            .NamespaceTypeName("players", "DispatchMutations")
            .Should()
            .Be("PlayersDispatchMutations");
    }

    [Test]
    public void PascalCase_CapitalizesFirstChar()
    {
        TrainTypeModule.PascalCase("alerts").Should().Be("Alerts");
        TrainTypeModule.PascalCase("Players").Should().Be("Players");
        TrainTypeModule.PascalCase("a").Should().Be("A");
    }

    [Test]
    public void CamelCase_LowercasesFirstChar()
    {
        TrainTypeModule.CamelCase("Alerts").Should().Be("alerts");
        TrainTypeModule.CamelCase("players").Should().Be("players");
        TrainTypeModule.CamelCase("A").Should().Be("a");
    }

    #endregion

    #region Empty Output Type — ObjectType skipped for types with no properties

    [Test]
    public async Task CreateTypesAsync_EmptyOutputType_DoesNotRegisterOutputObjectType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "EmptyOut",
                typeof(EmptyOutput),
                name: "EmptyOut",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // EmptyOutput has no properties, so ObjectType<EmptyOutput> should NOT be registered
        GetGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_EmptyOutputType_StillCreatesResponseType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "EmptyOut",
                typeof(EmptyOutput),
                name: "EmptyOut",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // Mutation response type is always created (externalId, metadataId, workQueueId)
        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_EmptyOutputType_StillCreatesExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "EmptyOut",
                typeof(EmptyOutput),
                name: "EmptyOut",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_MixedEmptyAndTypedOutput_OnlyTypedOutputRegistered()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "EmptyOut",
                typeof(EmptyOutput),
                name: "EmptyOut",
                serviceTypeName: "EmptyOut",
                operations: GraphQLOperation.Run
            ),
            CreateRegistration<TypedInput2>(
                "TypedOut",
                typeof(TypedOutput),
                name: "TypedOut",
                serviceTypeName: "TypedOut",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var outputTypes = GetGenericObjectTypes(types);
        outputTypes.Should().HaveCount(1);
        outputTypes[0].GetType().GetGenericArguments()[0].Should().Be(typeof(TypedOutput));
    }

    [Test]
    public async Task CreateTypesAsync_EmptyOutputQueryTrain_ReturnsGenericResponse()
    {
        // Query train with empty output should still generate (uses RunTrainResponse)
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "LookupTrain",
                typeof(EmptyOutput),
                name: "Lookup",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // No ObjectType<EmptyOutput> should be registered
        GetGenericObjectTypes(types).Should().BeEmpty();
        // Should still have DiscoverQueries and extensions
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_AllObjectPropertiesOutput_DoesNotRegisterOutputObjectType()
    {
        // Output type has properties, but they're all System.Object — HotChocolate ignores them
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "HitTrain",
                typeof(ObjectOnlyOutput),
                name: "HitTrain",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_AllObjectPropertiesOutput_StillCreatesResponseType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "HitTrain",
                typeof(ObjectOnlyOutput),
                name: "HitTrain",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_MixedObjectAndTypedProperties_RegistersOutputType()
    {
        // Output type has both object and non-object properties — should be registered
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "MixedTrain",
                typeof(MixedOutput),
                name: "MixedTrain",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetGenericObjectTypes(types).Should().HaveCount(1);
        GetGenericObjectTypes(types)[0]
            .GetType()
            .GetGenericArguments()[0]
            .Should()
            .Be(typeof(MixedOutput));
    }

    #endregion

    #region Unit Input — hard error at startup

    [Test]
    public void CreateTypesAsync_UnitInputMutationTrain_ThrowsInvalidOperationException()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<Unit>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var act = async () => await module.CreateTypesAsync(null!, CancellationToken.None);

        act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unit input*[TraxMutation]*");
    }

    [Test]
    public void CreateTypesAsync_UnitInputQueryTrain_ThrowsInvalidOperationException()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<Unit>(
                "GetStatus",
                typeof(TypedOutput),
                name: "GetStatus",
                isQuery: true,
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var act = async () => await module.CreateTypesAsync(null!, CancellationToken.None);

        act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unit input*[TraxQuery]*");
    }

    [Test]
    public void CreateTypesAsync_UnitInputTrain_ErrorMessageContainsTrainName()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<Unit>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var act = async () => await module.CreateTypesAsync(null!, CancellationToken.None);

        act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*dedicated input record*");
    }

    [Test]
    public void CreateTypesAsync_UnitInputAmongTypedTrains_StillThrows()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "CreatePlayer",
                typeof(TypedOutput),
                name: "CreatePlayer",
                serviceTypeName: "CreatePlayer",
                operations: GraphQLOperation.Run
            ),
            CreateRegistration<Unit>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                serviceTypeName: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var act = async () => await module.CreateTypesAsync(null!, CancellationToken.None);

        act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Empty Record Input — no InputObjectType but allowed for routing

    [Test]
    public async Task CreateTypesAsync_EmptyRecordInput_DoesNotRegisterInputObjectType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<EmptyInput>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            )
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_EmptyRecordInput_StillCreatesResponseType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<EmptyInput>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_EmptyRecordInput_StillCreatesExtensions()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<EmptyInput>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_EmptyRecordInputWithTypedOutput_RegistersOutputButNotInput()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<EmptyInput>(
                "GetStatus",
                typeof(TypedOutput),
                name: "GetStatus",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetGenericObjectTypes(types).Should().HaveCount(1);
        GetGenericObjectTypes(types)[0]
            .GetType()
            .GetGenericArguments()[0]
            .Should()
            .Be(typeof(TypedOutput));

        types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            )
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_MixedEmptyAndTypedInputTrains_OnlyRegistersTypedInputObjectType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<EmptyInput>(
                "RefreshCache",
                typeof(Unit),
                name: "RefreshCache",
                serviceTypeName: "RefreshCache",
                operations: GraphQLOperation.Run
            ),
            CreateRegistration<TypedInput>(
                "CreatePlayer",
                typeof(TypedOutput),
                name: "CreatePlayer",
                serviceTypeName: "CreatePlayer",
                operations: GraphQLOperation.Run
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        var inputTypes = types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            )
            .ToList();

        inputTypes.Should().HaveCount(1);
        inputTypes[0].GetType().GetGenericArguments()[0].Should().Be(typeof(TypedInput));
    }

    #endregion

    #region Helpers

    private static List<ITypeSystemMember> GetNonGenericObjectTypes(
        IReadOnlyCollection<ITypeSystemMember> types
    )
    {
        return types.Where(t => t.GetType() == typeof(ObjectType)).ToList();
    }

    private static readonly System.Collections.Generic.HashSet<Type> NamespaceMarkerTypes =
    [
        typeof(DiscoverQueries),
        typeof(DispatchMutations),
    ];

    private static List<ITypeSystemMember> GetGenericObjectTypes(
        IReadOnlyCollection<ITypeSystemMember> types
    )
    {
        return types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
                && !NamespaceMarkerTypes.Contains(t.GetType().GetGenericArguments()[0])
            )
            .ToList();
    }

    private static TrainRegistration CreateRegistration<TInput>(
        string trainName,
        Type outputType,
        string? name = null,
        string? serviceTypeName = null,
        string? description = null,
        string? deprecationReason = null,
        GraphQLOperation operations = GraphQLOperation.Run | GraphQLOperation.Queue,
        bool isQuery = false,
        string? graphqlNamespace = null
    )
    {
        return new TrainRegistration
        {
            ServiceType = typeof(IStubTrain),
            ImplementationType = typeof(StubTrain),
            InputType = typeof(TInput),
            OutputType = outputType,
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = serviceTypeName ?? trainName,
            ImplementationTypeName = trainName,
            InputTypeName = typeof(TInput).Name,
            OutputTypeName = outputType.Name,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsBroadcastEnabled = false,
            GraphQLName = name,
            GraphQLDescription = description,
            GraphQLDeprecationReason = deprecationReason,
            GraphQLOperations = operations,
            GraphQLNamespace = graphqlNamespace,
            IsRemote = false,
        };
    }

    #endregion

    #region Stubs

    private class StubDiscoveryService(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private interface IStubTrain;

    private class StubTrain;

    private interface IDisabled;

    private class Disabled;

    public record UnitInput
    {
        public string Name { get; init; } = "";
    }

    public record TypedInput
    {
        public string Value { get; init; } = "";
    }

    public record TypedInput2
    {
        public string Value { get; init; } = "";
    }

    public record SharedInput
    {
        public string Id { get; init; } = "";
    }

    public record TypedOutput
    {
        public string Result { get; init; } = "";
        public int Count { get; init; }
    }

    public record TypedOutput2
    {
        public string Other { get; init; } = "";
    }

    public record EmptyInput;

    public record EmptyOutput;

    public record ObjectOnlyOutput
    {
        public object? Source { get; init; }
        public object? Data { get; init; }
    }

    public record MixedOutput
    {
        public object? Source { get; init; }
        public string Name { get; init; } = "";
    }

    #endregion
}
