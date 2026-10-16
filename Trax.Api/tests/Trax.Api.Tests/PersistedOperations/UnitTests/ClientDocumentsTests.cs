using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.PersistedOperations.Services;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;
using Path = System.IO.Path;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// A host reads the documents its frontend wrote from <c>trax.documents.json</c>, refuses a file it cannot trust, a
/// document that reaches anything but the trains and query models it registered, and a filter or sort passed whole as
/// a variable, and stores each under an id carrying the hash of its text, the same id <c>trax client</c> gives it.
/// <para>Enforces <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md")]
public class ClientDocumentsTests
{
    private const string Adr =
        "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md";

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-client-docs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    #region Ids

    [Test]
    public void The_cli_and_the_host_hash_a_document_alike()
    {
        foreach (var (name, text, id) in FixtureCases())
            ClientDocuments.IdOf(name, text).Should().Be(id, $"the fixture's id for {name}");
    }

    [Test]
    public void A_client_id_is_reserved_and_reads_back_to_its_slot()
    {
        var id = ClientDocuments.IdOf("Order_Card", "query Order_Card { x }");

        id.Should().StartWith("trax_client_Order-Card_");
        GeneratedDocuments.IsReserved(id).Should().BeTrue();
        GeneratedDocuments.PrefixOf(id).Should().Be(ClientDocuments.ReservedIdPrefix);
        ClientDocuments.SlotOfId(id).Should().Be("trax_client_Order-Card");
        GeneratedDocuments
            .SlotOf(id, ["trax_client_Order-Card"])
            .Should()
            .Be("trax_client_Order-Card");
    }

    [Test]
    public void A_stored_row_whose_body_is_not_the_one_its_id_names_is_not_a_client_document()
    {
        var id = ClientDocuments.IdOf("OrderCard", "query OrderCard { a }");

        GeneratedDocuments.IsGenerated(id, "query OrderCard { a }").Should().BeTrue();
        GeneratedDocuments.IsGenerated(id, "query OrderCard { b }").Should().BeFalse();
    }

    [TestCase("trax_client__" + Hash)]
    [TestCase("trax_client_OrderCard")]
    [TestCase("trax_client_Order_Card_" + Hash)]
    public void An_id_that_is_not_a_client_document_names_no_slot(string id) =>
        ClientDocuments.SlotOfId(id).Should().BeNull();

    private const string Hash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabbbbbbbbbbbbbbbb";

    #endregion

    #region The file

    [Test]
    public void A_file_reads_back_in_name_order_with_its_ids()
    {
        var path = Write(
            """
            { "format": 1, "documents": [
              { "name": "Zed", "source": "src/zed.graphql", "text": "query Zed { a }" },
              { "name": "OrderCard", "source": "src/orderCard.graphql", "text": "query OrderCard { a }" }
            ] }
            """
        );

        var documents = ClientDocuments.Load(path);

        documents.Select(d => d.Name).Should().Equal("OrderCard", "Zed");
        documents[0].Id.Should().Be(ClientDocuments.IdOf("OrderCard", "query OrderCard { a }"));
        documents[0].Owner.Should().Be("client document 'OrderCard' (src/orderCard.graphql)");
    }

    [Test]
    public void A_missing_file_refuses_naming_it()
    {
        var path = Path.Combine(_dir, "nope.json");

        var act = () => ClientDocuments.Load(path);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*'{path}'*cannot be read*");
    }

    [TestCase("not json", "*not JSON trax client wrote*")]
    [TestCase("""{ "format": 2, "documents": [] }""", "*not format 1*")]
    [TestCase("""{ "documents": [] }""", "*not format 1*")]
    [TestCase("""{ "format": 1 }""", "*not format 1*")]
    [TestCase(
        """{ "format": 1, "documents": [ { "name": "A", "text": "" } ] }""",
        "*without a GraphQL name or a text*"
    )]
    [TestCase(
        """{ "format": 1, "documents": [ { "name": "a-b", "text": "query a { x }" } ] }""",
        "*without a GraphQL name*"
    )]
    [TestCase("""{ "format": 1, "documents": [ null ] }""", "*without a GraphQL name*")]
    [TestCase(
        """{ "format": 1, "documents": [ { "name": "A", "text": "query A { x }" }, { "name": "A", "text": "query A { y }" } ] }""",
        "*client document 'A'*twice*"
    )]
    public void A_file_it_cannot_trust_refuses(string json, string message)
    {
        var path = Write(json);

        var act = () => ClientDocuments.Load(path);

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    #endregion

    #region What a document may hold

    [TestCase(
        "query OrderCard($input: StartInput!) { discover { findOrder(input: $input) { name } } }"
    )]
    [TestCase(
        "query OrderCard($input: StartInput!) { discover { first: findOrder(input: $input) { name } "
            + "second: findOrder(input: $input) { name } } }"
    )]
    [TestCase(
        "query OrderCard($input: StartInput!) { discover { runs { findRun(input: $input) { name } } } }"
    )]
    [TestCase(
        "mutation OrderCard($input: StartInput!) { dispatch { startRun(input: $input) { externalId } } }"
    )]
    [TestCase(
        "query OrderCard($input: StartInput!) { discover { findOrder(input: $input) { ...Named } } } "
            + "fragment Named on Named { name }"
    )]
    [TestCase(
        "query OrderCard($input: StartInput!) {\n  discover {\n    findOrder(input: $input) {\n      ...Named\n    }\n"
            + "  }\n}\n\nfragment Named on Named {\n  name\n}\n"
    )]
    public async Task A_document_of_trains_is_accepted(string text)
    {
        var (schema, reach) = await SchemaAsync();

        ClientDocuments.Refusal(schema, reach, "OrderCard", text).Should().BeNull();
    }

    [TestCase("query OrderCard { _ping }", "*other than 'discover'*")]
    [TestCase("query OrderCard { __schema { types { name } } }", "*other than 'discover'*")]
    [TestCase(
        "query OrderCard { discover { __typename } }",
        "*'discover.__typename'*does not serve*"
    )]
    [TestCase(
        "query OrderCard { d: discover { findOrder(input: { scenario: \"x\" }) { name } } }",
        "*other than 'discover'*"
    )]
    [TestCase(
        "query OrderCard { discover { ... on DiscoverQueries { findOrder(input: { scenario: \"x\" }) { name } } } }",
        "*fragment directly under 'discover'*"
    )]
    [TestCase(
        "query OrderCard { discover { r: runs { findRun(input: { scenario: \"x\" }) { name } } } }",
        "*aliases the namespace 'runs'*"
    )]
    [TestCase(
        "query OrderCard { discover { runs { __typename } } }",
        "*other than a train under the namespace 'runs'*"
    )]
    [TestCase("query OrderCard { discover { runs } }", "*selects no train*")]
    [TestCase(
        "mutation OrderCard($input: StartInput!) { dispatch { startRun(input: $input) { externalId } "
            + "other(input: $input) { externalId } } }",
        "*runs 2 trains in one mutation*"
    )]
    [TestCase(
        "subscription OrderCard { runProgress(runId: \"x\") { runId } }",
        "*is a subscription*"
    )]
    [TestCase(
        "query OrderCard { discover { findOrder(input: { scenario: \"x\" }) { name @include(if: true) } } }",
        "*uses a directive*"
    )]
    [TestCase(
        "query OrderCard @cached { discover { findOrder(input: { scenario: \"x\" }) { name } } }",
        "*uses a directive*"
    )]
    [TestCase(
        "query OrderCard { discover { findOrder(input: { scenario: \"x\" }) { name } } } query Other { _ping }",
        "*holds 2 operations*"
    )]
    [TestCase(
        "{ discover { findOrder(input: { scenario: \"x\" }) { name } } }",
        "*registered as 'OrderCard'*"
    )]
    [TestCase(
        "query Other { discover { findOrder(input: { scenario: \"x\" }) { name } } }",
        "*registered as 'OrderCard'*named 'Other'*"
    )]
    [TestCase("query OrderCard { discover { findOrder(", "*is not GraphQL*")]
    [TestCase(
        "type X { a: Int } query OrderCard { discover { findOrder(input: { scenario: \"x\" }) { name } } }",
        "*neither an operation nor a fragment*"
    )]
    public async Task A_document_that_reaches_more_than_trains_is_refused(
        string text,
        string message
    )
    {
        var (schema, reach) = await SchemaAsync();

        ClientDocuments.Refusal(schema, reach, "OrderCard", text).Should().Match(message);
    }

    [TestCase(
        "query OrderCard($after: String) { discover { orders(first: 2, after: $after, where: { state: { eq: \"open\" } }, "
            + "order: { title: ASC }) { totalCount nodes { title } } } }"
    )]
    [TestCase(
        "query OrderCard($state: String, $states: [String]) { discover { orders(where: { state: { eq: $state }, "
            + "or: [{ state: { in: $states } }] }) { nodes { title } } } }"
    )]
    [TestCase(
        "query OrderCard($input: StartInput!) { discover { runs { ledger { title } findRun(input: $input) { name } } "
            + "orders { nodes { state } } } }"
    )]
    public async Task A_document_reading_a_query_model_is_accepted(string text)
    {
        var (schema, reach) = await SchemaAsync();

        ClientDocuments
            .Refusal(schema, reach, "OrderCard", text)
            .Should()
            .BeNull("a query model is reached from a client document, per " + Adr);
    }

    [TestCase(
        "query OrderCard { discover { notes } }",
        "*'discover.notes'*neither a train nor a query model*"
    )]
    [TestCase(
        "query OrderCard { discover { runs { memo } } }",
        "*'discover.runs.memo'*neither a train nor a query model*"
    )]
    [TestCase(
        "query OrderCard($where: OrderFilterInput) { discover { orders(where: $where) { nodes { title } } } }",
        "*'$where'*OrderFilterInput*filter or sort input*"
    )]
    [TestCase(
        "query OrderCard($order: [OrderSortInput!]) { discover { orders(order: $order) { nodes { title } } } }",
        "*'$order'*OrderSortInput*filter or sort input*"
    )]
    [TestCase(
        "mutation OrderCard { dispatch { orders { nodes { title } } } }",
        "*'dispatch.orders'*does not serve*"
    )]
    public async Task A_field_or_variable_a_client_document_may_not_hold_is_refused(
        string text,
        string message
    )
    {
        var (schema, reach) = await SchemaAsync();

        ClientDocuments
            .Refusal(schema, reach, "OrderCard", text)
            .Should()
            .Match(message, "the host resolves every field against what it registered, per " + Adr);
    }

    [Test]
    public async Task A_query_model_the_host_did_not_register_is_refused()
    {
        var (schema, _) = await SchemaAsync();
        var trainsOnly = ClientDocumentReach.Of([("query", null, "findOrder")], []);

        ClientDocuments
            .Refusal(
                schema,
                trainsOnly,
                "OrderCard",
                "query OrderCard { discover { orders { nodes { title } } } }"
            )
            .Should()
            .Match("*'discover.orders'*neither a train nor a query model*");
        ClientDocuments
            .Refusal(
                schema,
                ClientDocumentReach.None,
                "OrderCard",
                "query OrderCard($input: StartInput!) { discover { findOrder(input: $input) { name } } }"
            )
            .Should()
            .NotBeNull("a node that registered nothing reaches nothing");
    }

    [Test]
    public async Task Check_names_every_document_it_refuses()
    {
        var (schema, reach) = await SchemaAsync();
        var documents = new[]
        {
            new ClientDocument(
                "trax_client_A_x",
                "query A { _ping }",
                "A",
                "client document 'A' (a.graphql)"
            ),
            new ClientDocument(
                "trax_client_B_x",
                "query B { _ping }",
                "B",
                "client document 'B' (b.graphql)"
            ),
        };

        var act = () => ClientDocuments.Check(schema, reach, documents);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*client document 'A' (a.graphql)*client document 'B' (b.graphql)*");
    }

    #endregion

    #region Fixtures

    public record StartInput
    {
        public string Scenario { get; init; } = "";
    }

    public record Named
    {
        public string Name { get; init; } = "";
    }

    public record Order
    {
        public string Title { get; init; } = "";
        public string State { get; init; } = "";
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    internal static IEnumerable<(string Name, string Text, string Id)> FixtureCases()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var candidate = Path.Combine(
                dir.FullName,
                "Trax.Api",
                "tests",
                "Trax.Api.Tests",
                "PersistedOperations",
                "Fixtures",
                "client-document-ids.json"
            );
            if (!File.Exists(candidate))
                continue;
            using var json = JsonDocument.Parse(File.ReadAllText(candidate));
            return json
                .RootElement.GetProperty("cases")
                .EnumerateArray()
                .Select(c =>
                    (
                        c.GetProperty("name").GetString()!,
                        c.GetProperty("text").GetString()!,
                        c.GetProperty("id").GetString()!
                    )
                )
                .ToList();
        }
        Assert.Fail("client-document-ids.json not found above the test's directory.");
        return [];
    }

    // The trains below, two query models (orders, paged with a filter and a sort, at the root; ledger, a plain list, in
    // the runs namespace beside a train) and two hand-written fields the host did not register (notes at the root, memo
    // under runs), which the schema serves but no client document may reach.
    private static async Task<(ISchemaDefinition Schema, ClientDocumentReach Reach)> SchemaAsync()
    {
        TrainRegistration[] registrations =
        [
            Registration("FindOrder", typeof(Named), isQuery: true, graphqlNamespace: null),
            Registration("FindRun", typeof(Named), isQuery: true, graphqlNamespace: "runs"),
            Registration("StartRun", typeof(Named), isQuery: false, graphqlNamespace: null),
            Registration("Other", typeof(Named), isQuery: false, graphqlNamespace: null),
        ];
        var services = new ServiceCollection();
        services.AddSingleton<ITrainDiscoveryService>(new StubDiscovery(registrations));
        services.AddSingleton<TrainTypeModule>();
        services
            .AddGraphQLServer("trax")
            .AddQueryType(d =>
                d.Name("RootQuery").Field("_ping").Type<StringType>().Resolve("pong")
            )
            .AddTypeModule<TrainTypeModule>()
            .AddMutationType<RootMutation>()
            .AddTypeExtension(
                new ObjectTypeExtension(d =>
                    d.Name("RootMutation").Field("_ping").Type<StringType>().Resolve("pong")
                )
            )
            .AddFiltering()
            .AddSorting()
            .AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("DiscoverQueries");
                    d.Field("orders")
                        .Type<ListType<NonNullType<ObjectType<Order>>>>()
                        .UsePaging<ObjectType<Order>>(options: new() { IncludeTotalCount = true })
                        .UseFiltering()
                        .UseSorting()
                        .Resolve(Array.Empty<Order>());
                    d.Field("notes").Type<StringType>().Resolve("hand-written");
                })
            )
            .AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RunsDiscoverQueries");
                    d.Field("ledger")
                        .Type<ListType<NonNullType<ObjectType<Order>>>>()
                        .Resolve(Array.Empty<Order>());
                    d.Field("memo").Type<StringType>().Resolve("hand-written");
                })
            );
        var executor = await services
            .BuildServiceProvider()
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
        var reach = ClientDocumentReach.Of(
            [
                ("query", null, "findOrder"),
                ("query", "runs", "findRun"),
                ("mutation", null, "startRun"),
                ("mutation", null, "other"),
            ],
            [(null, "orders"), ("runs", "ledger")]
        );
        return (executor.Schema, reach);
    }

    private static TrainRegistration Registration(
        string name,
        Type output,
        bool isQuery,
        string? graphqlNamespace
    ) =>
        new()
        {
            ServiceType = typeof(IStubTrain),
            ImplementationType = typeof(StubTrain),
            InputType = typeof(StartInput),
            OutputType = output,
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = name,
            ImplementationTypeName = name,
            InputTypeName = nameof(StartInput),
            OutputTypeName = output.Name,
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsBroadcastEnabled = false,
            GraphQLName = name,
            GraphQLOperations = GraphQLOperation.Run,
            GraphQLNamespace = graphqlNamespace,
            IsRemote = false,
        };

    private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> registrations)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => registrations;
    }

    private interface IStubTrain;

    private sealed class StubTrain;

    #endregion
}
