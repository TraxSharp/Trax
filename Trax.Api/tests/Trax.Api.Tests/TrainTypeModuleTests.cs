using FluentAssertions;
using HotChocolate.Types;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
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
        bool isQuery = false
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

    #endregion
}
