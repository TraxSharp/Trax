using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// The types a client manifest lists are the ones its documents need: never a type that holds operations
/// (<c>dispatch</c>, <c>discover</c> and their namespaces), so a query model's fields and their filter inputs stay on
/// the server, and every type a listed one names is listed too.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class ClientManifestTests
{
    private static readonly HashSet<string> BuiltIns = new(StringComparer.Ordinal)
    {
        "String",
        "Int",
        "Float",
        "Boolean",
        "ID",
    };

    [Test]
    public async Task Manifest_types_hold_no_wrapper_type_and_no_dangling_reference()
    {
        var (schema, trains) = await SchemaAsync();

        var manifest = ClientManifestExport.Build(schema, [], trains);
        var definitions = Utf8GraphQLParser
            .Parse(manifest.Types)
            .Definitions.OfType<INamedSyntaxNode>()
            .ToList();
        var defined = definitions.Select(d => d.Name.Value).ToHashSet(StringComparer.Ordinal);

        defined
            .Should()
            .NotContain(DocumentTypes.Wrappers(schema))
            .And.NotContain("OrderFilterInput", "a query model's filter is not a train's type")
            .And.Contain(["StartInput", "RunOutput"]);
        manifest.Types.Should().NotContain("orders", "a query model's field stays on the server");
        definitions
            .SelectMany(Referenced)
            .Where(name => !BuiltIns.Contains(name))
            .Should()
            .OnlyContain(name => defined.Contains(name), "every type a listed one names is listed");
        manifest.Trains.Select(t => t.Answer).Should().AllSatisfy(a => a.Should().NotBeNull());
    }

    [Test]
    public async Task The_manifest_sdl_holds_the_types_of_left_out_fields()
    {
        var (schema, trains) = await SchemaAsync();

        var manifest = ClientManifestExport.Build(schema, [], trains);
        var defined = Utf8GraphQLParser
            .Parse(manifest.Types)
            .Definitions.OfType<INamedSyntaxNode>()
            .Select(d => d.Name.Value)
            .ToList();

        manifest
            .Trains.Single(t => t.Field == "findChild")
            .Documents[0]
            .Document.Should()
            .NotContain("edges", "the paged back-reference is left out of the generated document");
        defined
            .Should()
            .Contain(
                n => n.EndsWith("Connection", StringComparison.Ordinal),
                "a client document may select the left-out paged field, so its connection type is listed"
            )
            .And.Contain(n => n.EndsWith("Edge", StringComparison.Ordinal))
            .And.Contain("PageInfo")
            .And.NotContain(DocumentTypes.Wrappers(schema));
    }

    private static IEnumerable<string> Referenced(INamedSyntaxNode node) =>
        node switch
        {
            ComplexTypeDefinitionNodeBase complex => complex
                .Fields.SelectMany(f =>
                    f.Arguments.Select(a => a.Type.NamedType().Name.Value)
                        .Append(f.Type.NamedType().Name.Value)
                )
                .Concat(complex.Interfaces.Select(i => i.Name.Value)),
            InputObjectTypeDefinitionNode input => input.Fields.Select(f =>
                f.Type.NamedType().Name.Value
            ),
            UnionTypeDefinitionNode union => union.Types.Select(t => t.Name.Value),
            _ => [],
        };

    private static async Task<(
        ISchemaDefinition Schema,
        IReadOnlyDictionary<TrainRegistration, string> Trains
    )> SchemaAsync()
    {
        TrainRegistration[] registrations =
        [
            Registration("StartRun", isQuery: false),
            Registration("FindRun", isQuery: true),
            Registration("FindChild", isQuery: true, typeof(PagedChild)),
        ];
        var services = new ServiceCollection();
        services.AddSingleton<ITrainDiscoveryService>(new StubDiscovery(registrations));
        services.AddSingleton<TrainTypeModule>();
        var executor = await services
            .AddGraphQLServer("trax")
            .AddQueryType(d =>
                d.Name("RootQuery").Field("_ping").Type<StringType>().Resolve("pong")
            )
            .AddMutationType<RootMutation>()
            .AddTypeExtension(
                new ObjectTypeExtension(d =>
                    d.Name("RootMutation").Field("_ping").Type<StringType>().Resolve("pong")
                )
            )
            .AddTypeModule<TrainTypeModule>()
            // What a query model adds under discover: a field over every row, with a filter input.
            .AddTypeExtension(
                new ObjectTypeExtension(d =>
                    d.Name("DiscoverQueries")
                        .Field("orders")
                        .Argument("where", a => a.Type<InputObjectType<OrderFilterInput>>())
                        .Type<ListType<NonNullType<ObjectType<Order>>>>()
                        .Resolve(Array.Empty<Order>())
                )
            )
            .Services.BuildServiceProvider()
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        return (executor.Schema, TrainTypeModule.AssignTrainNames(registrations));
    }

    public record StartInput
    {
        public string Scenario { get; init; } = "";
    }

    public record RunOutput
    {
        public string RunId { get; init; } = "";
    }

    public record PagedParent
    {
        public string Name { get; init; } = "";

        [UsePaging]
        public PagedChild[] Children { get; init; } = [];
    }

    public record PagedChild
    {
        public string Name { get; init; } = "";
        public PagedParent? Parent { get; init; }
    }

    public record Order
    {
        public string Id { get; init; } = "";
    }

    public record OrderFilterInput
    {
        public string? IdEquals { get; init; }
    }

    private static TrainRegistration Registration(string name, bool isQuery, Type? output = null) =>
        new()
        {
            ServiceType = typeof(IStubTrain),
            ImplementationType = typeof(StubTrain),
            InputType = typeof(StartInput),
            OutputType = output ?? typeof(RunOutput),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = name,
            ImplementationTypeName = name,
            InputTypeName = nameof(StartInput),
            OutputTypeName = (output ?? typeof(RunOutput)).Name,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsBroadcastEnabled = false,
            GraphQLName = name,
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private interface IStubTrain;

    private sealed class StubTrain;
}
