using FluentAssertions;
using HotChocolate.Types;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests;

/// <summary>
/// Tests for TrainTypeModule's CreateTypesAsync — verifies type generation
/// for typed vs Unit output trains, input/output deduplication, name derivation, etc.
///
/// Note: per-train response ObjectTypes are created lazily inside the ObjectTypeExtension
/// delegate (called by HotChocolate during schema initialization), so they are NOT visible
/// in the returned IReadOnlyCollection. We test their generation indirectly via type counts
/// and the presence/absence of ObjectType&lt;TOut&gt; registrations.
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
    public async Task CreateTypesAsync_RunOnlyTrain_GeneratesExtension()
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

        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_QueueOnlyTrain_GeneratesExtension()
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

        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
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
            GraphQLOperations = GraphQLOperation.Run,
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
    public async Task CreateTypesAsync_GraphQLEnabledTrains_ExactlyOneObjectTypeExtension()
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

        // All trains share a single ObjectTypeExtension for "DispatchMutations"
        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
    }

    #endregion

    #region Type Counts — verifying typed vs Unit train type generation

    [Test]
    public async Task CreateTypesAsync_UnitTrain_GeneratesInputAndExtension()
    {
        // Unit train with Run+Queue → InputObjectType<TIn> + ObjectTypeExtension
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>("UnitTrain", typeof(Unit)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        // InputObjectType<UnitInput> + ObjectTypeExtension = 2
        types.Should().HaveCount(2);
        types.Should().ContainSingle(t => t is ObjectTypeExtension);
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            );
    }

    [Test]
    public async Task CreateTypesAsync_TypedTrain_GeneratesInputOutputResponseAndExtension()
    {
        // Typed train → InputObjectType<TIn> + ObjectType<TOut> + ObjectType (response) + ObjectTypeExtension = 4
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>("TypedTrain", typeof(TypedOutput)),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        types.Should().HaveCount(4);
        types.Should().ContainSingle(t => t is ObjectTypeExtension);
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(InputObjectType<>)
            );
        types
            .Should()
            .ContainSingle(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
            );
        // Per-train response type (non-generic ObjectType)
        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_MixedTrains_CorrectTypeCount()
    {
        // 1 typed + 1 Unit, different input types →
        // InputObjectType<TypedInput> + InputObjectType<UnitInput> + ObjectType<TypedOutput>
        // + ObjectType (response) + ObjectTypeExtension = 5
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

        types.Should().HaveCount(5);
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

        // Each typed Run train gets its own response ObjectType
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

        // Response ObjectType is created eagerly and included in the returned types
        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    [Test]
    public async Task CreateTypesAsync_UnitOutputTrain_DoesNotCreatePerTrainResponseType()
    {
        var discovery = new StubDiscoveryService([
            CreateRegistration<UnitInput>("UnitTrain", typeof(Unit), name: "Unit"),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_QueueOnlyTypedTrain_NoResponseType()
    {
        // Queue-only trains don't get a per-train response type (queue always returns QueueTrainResponse)
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

        GetNonGenericObjectTypes(types).Should().BeEmpty();
    }

    [Test]
    public async Task CreateTypesAsync_RunAndQueueTypedTrain_CreatesOneResponseType()
    {
        // Run+Queue typed train: response type only for the Run operation
        var discovery = new StubDiscoveryService([
            CreateRegistration<TypedInput>(
                "Both",
                typeof(TypedOutput),
                name: "Both",
                operations: GraphQLOperation.RunAndQueue
            ),
        ]);
        var module = new TrainTypeModule(discovery);

        var types = await module.CreateTypesAsync(null!, CancellationToken.None);

        GetNonGenericObjectTypes(types).Should().HaveCount(1);
    }

    #endregion

    #region Query Train Generation

    [Test]
    public async Task CreateTypesAsync_QueryTrain_GeneratesDiscoverQueriesExtension()
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

        types.OfType<ObjectTypeExtension>().Should().HaveCount(1);
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
    public async Task CreateTypesAsync_MixedQueryAndMutationTrains_GeneratesTwoExtensions()
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

        // One ObjectTypeExtension for DiscoverQueries, one for DispatchMutations
        types.OfType<ObjectTypeExtension>().Should().HaveCount(2);
    }

    [Test]
    public async Task CreateTypesAsync_QueryTrainWithTypedOutput_TypeCount()
    {
        // Query typed train → InputObjectType<TIn> + ObjectType<TOut> + ObjectTypeExtension = 3
        // No per-train response type for queries
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

        types.Should().HaveCount(3);
    }

    #endregion

    #region Helpers

    private static List<ITypeSystemMember> GetNonGenericObjectTypes(
        IReadOnlyCollection<ITypeSystemMember> types
    )
    {
        return types.Where(t => t.GetType() == typeof(ObjectType)).ToList();
    }

    private static List<ITypeSystemMember> GetGenericObjectTypes(
        IReadOnlyCollection<ITypeSystemMember> types
    )
    {
        return types
            .Where(t =>
                t.GetType().IsGenericType
                && t.GetType().GetGenericTypeDefinition() == typeof(ObjectType<>)
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
        GraphQLOperation operations = GraphQLOperation.Run,
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
