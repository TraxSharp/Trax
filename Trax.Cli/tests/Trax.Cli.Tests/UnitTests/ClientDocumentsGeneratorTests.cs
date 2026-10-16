using System.Text.Json;
using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Commands;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax client --documents</c> reads the <c>.graphql</c> files a frontend wrote, refuses one the host would refuse
/// (anything but trains and query models, a filter or sort passed as a variable, an unknown field, a variable out of
/// place, a directive, a fragment that cannot apply), types
/// each operation's result from what it selects, and writes the <c>trax.documents.json</c> the host registers, under
/// the ids the host gives the same text.
/// <para>Enforces <c>Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md")]
public class ClientDocumentsGeneratorTests
{
    private const string Adr =
        "Trax.Docs/adr/0053-a-client-document-is-registered-by-the-host-at-boot.md";

    private const string Types = """
        enum Level {
          LOW
          HIGH
        }

        input FindInput {
          id: Int!
        }

        input NoteInput {
          text: String!
        }

        type Order {
          title: String!
          level: Level
          customer: Customer!
        }

        type Customer {
          name: String! @cost(weight: "10")
          orders: [Order!]!
          ordersPage(first: Int): OrderConnection
          orderById(id: Int!): Order
        }

        type OrderConnection {
          edges: [OrderEdge!]
          nodes: [Order!]
          pageInfo: PageInfo!
        }

        type OrderEdge {
          cursor: String!
          node: Order!
        }

        type PageInfo {
          hasNextPage: Boolean!
        }

        type NoteResponse {
          externalId: String!
          output: Note
        }

        type Note {
          text: String!
        }

        union Found = Order | Customer

        type OrdersConnection {
          nodes: [Order!]
          pageInfo: PageInfo!
          totalCount: Int!
        }

        input OrderFilterInput {
          and: [OrderFilterInput!]
          or: [OrderFilterInput!]
          title: StringOperationFilterInput
          level: LevelOperationFilterInput
        }

        input StringOperationFilterInput {
          eq: String
          in: [String]
        }

        input LevelOperationFilterInput {
          eq: Level
        }

        input OrderSortInput {
          title: SortEnumType
        }

        enum SortEnumType {
          ASC
          DESC
        }
        """;

    private static readonly ClientManifest Manifest = new()
    {
        Trains =
        [
            Train(
                "query",
                null,
                "findOrder",
                "Order!",
                "query FindOrder($input: FindInput!) { discover { findOrder(input: $input) { title } } }"
            ),
            Train(
                "query",
                "runs",
                "findRun",
                "Order!",
                "query RunsFindRun($input: FindInput!) { discover { runs { findRun(input: $input) { title } } } }"
            ),
            Train(
                "query",
                null,
                "search",
                "[Found!]!",
                "query Search($input: FindInput!) { discover { search(input: $input) { __typename } } }"
            ),
            Train(
                "mutation",
                null,
                "recordNote",
                "NoteResponse!",
                "mutation RecordNote($input: NoteInput!, $priority: Int) { dispatch { recordNote(input: $input, priority: $priority) { externalId } } }"
            ),
            Train(
                "mutation",
                null,
                "other",
                "NoteResponse!",
                "mutation Other($input: NoteInput!) { dispatch { other(input: $input) { externalId } } }"
            ),
        ],
        QueryModels =
        [
            new ClientManifestQueryModel
            {
                Field = "orders",
                Answer = "OrdersConnection",
                Arguments =
                [
                    new ClientManifestArgument { Name = "first", Type = "Int" },
                    new ClientManifestArgument { Name = "after", Type = "String" },
                    new ClientManifestArgument { Name = "where", Type = "OrderFilterInput" },
                    new ClientManifestArgument { Name = "order", Type = "[OrderSortInput!]" },
                ],
                Requires = new ClientManifestRequirement
                {
                    RoleSets =
                    [
                        ["clerk"],
                    ],
                },
            },
            // A plain list, in a namespace a train shares.
            new ClientManifestQueryModel
            {
                Field = "ledger",
                Namespace = "runs",
                Answer = "[Order!]!",
            },
            // The only thing in its namespace.
            new ClientManifestQueryModel
            {
                Field = "catalog",
                Namespace = "shop",
                Answer = "[Order!]!",
            },
        ],
        FilterInputs =
        [
            "LevelOperationFilterInput",
            "OrderFilterInput",
            "OrderSortInput",
            "StringOperationFilterInput",
        ],
        Types = Types,
    };

    private const string OrderCard = """
        query OrderCard($input: FindInput!) {
          discover {
            findOrder(input: $input) {
              title
              customer { name }
            }
          }
        }
        """;

    #region What it writes

    [Test]
    public void A_document_selecting_a_query_model_returns_exactly_that()
    {
        var files = Render(
            (
                "src/orderList.graphql",
                "query OrderList($level: Level, $after: String) { discover { orders(first: 2, after: $after, "
                    + "where: { level: { eq: $level } }, order: [{ title: ASC }]) { totalCount nodes { title } } } }"
            )
        );
        var module = Text(files, "documents/orderList.g.ts");

        module
            .Should()
            .Contain(
                "export function orderList(options: { level?: Level | null; after?: string | null } = {}): Promise<OrderListResult>"
            )
            .And.Contain("totalCount: number;")
            .And.Contain("nodes: Array<{\n    title: string;\n  }> | null;")
            .And.Contain("[\"discover\", \"orders\"]")
            .And.Contain("holding one of the roles clerk");
        Text(files, "types.g.ts")
            .Should()
            .Contain("export type Level")
            .And.NotContain(
                "OrderFilterInput",
                "a filter is written in the document, so no variable has its type"
            );
        Json(files)
            .GetProperty("documents")[0]
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("OrderList");
    }

    [Test]
    public void A_query_with_a_train_and_a_query_model_answers_both()
    {
        var module = Text(
            Render(
                (
                    "src/both.graphql",
                    "query Both($input: FindInput!) { discover { findOrder(input: $input) { title } "
                        + "orders { totalCount } runs { ledger { title } } } }"
                )
            ),
            "documents/both.g.ts"
        );

        module
            .Should()
            .Contain("findOrder: {")
            .And.Contain("orders: {")
            .And.Contain("ledger: Array<{");
    }

    [Test]
    public void A_namespace_holding_only_query_models_resolves()
    {
        var render = () =>
            Render(("src/shop.graphql", "query Shop { discover { shop { catalog { title } } } }"));

        render.Should().NotThrow("a namespace counts when a query model is in it");
    }

    [Test]
    public void The_editor_schema_holds_the_query_models_under_their_namespaces()
    {
        var schema = Text(Render(("src/orderCard.graphql", OrderCard)), "schema.graphql");

        schema
            .Should()
            .Contain(
                "orders(first: Int, after: String, where: OrderFilterInput, order: [OrderSortInput!]): OrdersConnection"
            )
            .And.Contain("ledger: [Order!]!")
            .And.Contain("catalog: [Order!]!");
    }

    [Test]
    public void A_document_selecting_a_subset_returns_exactly_that()
    {
        var files = Render(("src/trax/orderCard.graphql", OrderCard));
        var module = Text(files, "documents/orderCard.g.ts");

        module
            .Should()
            .Contain(
                """
                export type OrderCardResult = {
                  title: string;
                  customer: {
                    name: string;
                  };
                };
                """
            )
            .And.Contain("export function orderCard(input: FindInput): Promise<OrderCardResult>")
            .And.Contain("[\"discover\", \"findOrder\"]")
            .And.Contain("import type { FindInput } from \"../types.g\";");
        Text(files, "index.g.ts")
            .Should()
            .Contain(
                "export { orderCard, type OrderCardResult } from \"./documents/orderCard.g\";"
            );

        var json = Json(files);
        var entry = json.GetProperty("documents").EnumerateArray().Single();
        entry.GetProperty("name").GetString().Should().Be("OrderCard");
        entry.GetProperty("source").GetString().Should().Be("src/trax/orderCard.graphql");
        var text = entry.GetProperty("text").GetString()!;
        module
            .Should()
            .Contain(
                $"export const orderCardDocumentId = \"{ClientDocumentSet.IdOf("OrderCard", text)}\";",
                "the function sends the id the host gives the text it registers"
            );
        json.GetProperty("format").GetInt32().Should().Be(1);
    }

    [Test]
    public void The_cli_and_the_host_hash_a_document_alike()
    {
        foreach (var (name, text, id) in FixtureCases())
            ClientDocumentSet.IdOf(name, text).Should().Be(id, $"the fixture's id for {name}");
    }

    [Test]
    public void Reformatting_a_document_does_not_move_its_id_and_changing_a_field_does()
    {
        var compact =
            "query OrderCard($input: FindInput!) { discover { findOrder(input: $input) { title customer { name } } } }";
        var commented = "# the order card\n" + OrderCard.Replace("title", "title # heading");
        var changed = compact.Replace("title", "level");

        var a = Id(Render(("a.graphql", compact)));
        Id(Render(("a.graphql", OrderCard))).Should().Be(a);
        Id(Render(("a.graphql", commented))).Should().Be(a);
        Id(Render(("a.graphql", changed))).Should().NotBe(a);
    }

    [Test]
    public void A_query_of_two_trains_answers_both_under_their_keys()
    {
        var module = Text(
            Render(
                (
                    "two.graphql",
                    "query Two($input: FindInput!) { discover { findOrder(input: $input) { title } "
                        + "runs { findRun(input: $input) { level } } } }"
                )
            ),
            "documents/two.g.ts"
        );

        module
            .Should()
            .Contain(
                """
                export type TwoResult = {
                  findOrder: {
                    title: string;
                  };
                  runs: {
                    findRun: {
                      level: Level | null;
                    };
                  };
                };
                """
            )
            .And.Contain("[\"discover\"]")
            .And.Contain("import type { FindInput, Level } from \"../types.g\";");
    }

    [Test]
    public void Two_aliases_of_one_field_are_two_keys()
    {
        var module = Text(
            Render(
                (
                    "pair.graphql",
                    "query Pair($a: FindInput!, $b: FindInput!) { discover { first: findOrder(input: $a) { title } "
                        + "second: findOrder(input: $b) { heading: title } } }"
                )
            ),
            "documents/pair.g.ts"
        );

        module
            .Should()
            .Contain("  first: {")
            .And.Contain("  second: {")
            .And.Contain("    heading: string;")
            .And.Contain("export function pair(a: FindInput, b: FindInput): Promise<PairResult>");
    }

    [Test]
    public void A_fragment_in_another_file_is_carried_by_every_document_that_spreads_it()
    {
        var files = Render(
            ("fragments/badge.graphql", "fragment CustomerBadge on Customer { name }"),
            (
                "a.graphql",
                "query A($input: FindInput!) { discover { findOrder(input: $input) { title customer { ...CustomerBadge } } } }"
            ),
            (
                "b.graphql",
                "query B($input: FindInput!) { discover { runs { findRun(input: $input) { customer { ...CustomerBadge orders { title } } } } } }"
            )
        );

        Text(files, "fragments.g.ts")
            .Should()
            .Contain(
                """
                export type CustomerBadge = {
                  name: string;
                };
                """
            );
        Text(files, "index.g.ts")
            .Should()
            .Contain("export type { CustomerBadge } from \"./fragments.g\";");
        Text(files, "documents/b.g.ts")
            .Should()
            .Contain(
                """
                  customer: {
                    name: string;
                    orders: Array<{
                      title: string;
                    }>;
                  };
                """,
                "a spread's fields are written beside the others, so a component typed CustomerBadge takes it"
            );
        foreach (var entry in Json(files).GetProperty("documents").EnumerateArray())
            entry
                .GetProperty("text")
                .GetString()
                .Should()
                .Contain(
                    "fragment CustomerBadge on Customer",
                    "a persisted document is self-contained"
                );
    }

    [Test]
    public void An_inline_fragment_on_a_union_member_types_that_member()
    {
        Text(
                Render(
                    (
                        "s.graphql",
                        "query S($input: FindInput!) { discover { search(input: $input) { __typename ... on Order { title } } } }"
                    )
                ),
                "documents/s.g.ts"
            )
            .Should()
            .Contain(
                """
                export type SResult = Array<{
                  __typename: "Customer";
                } | {
                  __typename: "Order";
                  title: string;
                }>;
                """
            );
    }

    [Test]
    public void A_back_reference_may_be_selected_explicitly_paged_or_not()
    {
        var module = Text(
            Render(
                (
                    "deep.graphql",
                    "query Deep($input: FindInput!, $first: Int) { discover { findOrder(input: $input) { customer { "
                        + "orders { title } ordersPage(first: $first) { nodes { title } pageInfo { hasNextPage } } "
                        + "orderById(id: 3) { title } } } } }"
                )
            ),
            "documents/deep.g.ts"
        );

        module
            .Should()
            .Contain("    orders: Array<{")
            .And.Contain("    ordersPage: {")
            .And.Contain("      nodes: Array<{")
            .And.Contain("    orderById: {")
            .And.Contain(
                "export function deep(input: FindInput, options: { first?: number | null } = {})"
            );
    }

    [Test]
    public void A_required_argument_may_be_a_variable()
    {
        Text(
                Render(
                    (
                        "byId.graphql",
                        "query ById($input: FindInput!, $id: Int!) { discover { findOrder(input: $input) { customer { orderById(id: $id) { title } } } } }"
                    )
                ),
                "documents/byId.g.ts"
            )
            .Should()
            .Contain("export function byId(input: FindInput, id: number): Promise<ByIdResult>");
    }

    [Test]
    public void A_mutation_sends_its_train_and_optional_variables_with_their_defaults()
    {
        Text(
                Render(
                    (
                        "note.graphql",
                        "mutation Note($input: NoteInput!, $priority: Int = 2) { dispatch { recordNote(input: $input, priority: $priority) { output { text } } } }"
                    )
                ),
                "documents/note.g.ts"
            )
            .Should()
            .Contain(
                "export function note(input: NoteInput, options: { priority?: number | null } = {})"
            )
            .And.Contain("[\"dispatch\", \"recordNote\"]");
    }

    [Test]
    public void The_editor_gets_a_schema_holding_the_trains_and_a_config_naming_the_globs()
    {
        var files = new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(
                Manifest,
                Documents: [new ClientDocumentFile("src/a.graphql", OrderCard)],
                DocumentGlobs: ["../**/*.graphql"]
            )
        );

        Text(files, "schema.graphql")
            .Should()
            .Contain("type TraxQueryDiscover {")
            .And.Contain("  findOrder(input: FindInput!): Order!")
            .And.Contain("  runs: TraxQueryDiscoverRuns")
            .And.Contain("  recordNote(input: NoteInput!, priority: Int): NoteResponse!")
            .And.Contain("  query: TraxQuery\n  mutation: TraxMutation")
            .And.Contain("type Customer {\n  name: String!\n")
            .And.NotContain("@cost", "the editor's schema declares no directive, so it uses none");
        Text(files, "graphql.config.yml")
            .Should()
            .Be(
                "# Generated by trax client, for the GraphQL editor extension. Do not edit.\n"
                    + "schema: schema.graphql\ndocuments:\n  - \"../**/*.graphql\"\nexclude:\n  - schema.graphql\n"
            );
    }

    [Test]
    public void One_input_writes_the_same_bytes()
    {
        (string, string)[] documents =
        [
            ("fragments/badge.graphql", "fragment CustomerBadge on Customer { name }"),
            (
                "a.graphql",
                "query A($input: FindInput!) { discover { findOrder(input: $input) { customer { ...CustomerBadge } } } }"
            ),
            ("b.graphql", OrderCard),
        ];

        Render(documents)
            .Should()
            .BeEquivalentTo(Render([.. documents.Reverse()]), o => o.WithStrictOrdering());
    }

    [Test]
    public void Without_documents_none_of_their_files_are_written()
    {
        var files = new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(Manifest)
        );

        files
            .Select(f => f.Path)
            .Should()
            .NotContain([
                "trax.documents.json",
                "schema.graphql",
                "graphql.config.yml",
                "fragments.g.ts",
            ])
            .And.NotContain(p => p.StartsWith("documents/", StringComparison.Ordinal));
    }

    #endregion

    #region What it refuses

    [TestCase("query A { discover { findOrder(", "*a.graphql:1:*not GraphQL*")]
    [TestCase(
        "{ discover { findOrder(input: { id: 1 }) { title } } }",
        "*a.graphql:1:1: the operation has no name*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { title } } } query B { discover { findOrder(input: { id: 2 }) { title } } }",
        "*a.graphql:1:*second operation*"
    )]
    [TestCase("subscription A { runProgress(runId: \"x\") { runId } }", "*'A' is a subscription*")]
    [TestCase(
        "query A { operations { __typename } }",
        "*'A' selects something other than 'discover' at its root*"
    )]
    [TestCase("query A { persistedOperations { __typename } }", "*other than 'discover'*")]
    [TestCase("query A { machines { __typename } }", "*other than 'discover'*")]
    [TestCase("query A { runProgress(runId: \"x\") { runId } }", "*other than 'discover'*")]
    [TestCase("query A { __schema { types { name } } }", "*other than 'discover'*")]
    [TestCase("query A { __type(name: \"Order\") { name } }", "*other than 'discover'*")]
    [TestCase("query A { _ping }", "*other than 'discover'*")]
    [TestCase(
        "query A { d: discover { findOrder(input: { id: 1 }) { title } } }",
        "*other than 'discover'*"
    )]
    [TestCase(
        "query A { discover { nothing { title } } }",
        "*'discover.nothing' is no train, query model or namespace*"
    )]
    [TestCase(
        "query A { discover { runs { nothing { title } } } }",
        "*other than a train or query model under the namespace 'runs'*"
    )]
    [TestCase(
        "query A { discover { r: runs { findRun(input: { id: 1 }) { title } } } }",
        "*namespace 'runs' takes no alias*"
    )]
    [TestCase(
        "query A { discover { ... on X { title } } }",
        "*spreads a fragment directly under 'discover'*"
    )]
    [TestCase(
        "mutation A($input: NoteInput!) { dispatch { recordNote(input: $input) { externalId } other(input: $input) { externalId } } }",
        "*runs 2 trains in one mutation*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { colour } } }",
        "*a.graphql:1:*Order has no field 'colour'*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { customer } } }",
        "*Order.customer' is an object (Customer); select its fields*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { title { x } } } }",
        "*Order.title' is a String, which has no fields*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }, mode: RUN) { title } } }",
        "*'findOrder' takes no argument 'mode'*"
    )]
    [TestCase(
        "query A { discover { findOrder { title } } }",
        "*'findOrder' requires the argument 'input'*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { customer { orderById { title } } } } }",
        "*Customer.orderById requires the argument 'id'*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { customer { ordersPage(last: 2) { nodes { title } } } } } }",
        "*Customer.ordersPage takes no argument 'last'*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: $input) { title } } }",
        "*'$input' is used by*does not declare it*"
    )]
    [TestCase(
        "query A($input: FindInput!, $spare: Int) { discover { findOrder(input: $input) { title } } }",
        "*declares the variable '$spare' and never uses it*"
    )]
    [TestCase(
        "query A($input: NoteInput!) { discover { findOrder(input: $input) { title } } }",
        "*'$input' is NoteInput!, but the argument 'input' of 'findOrder' takes FindInput!*"
    )]
    [TestCase(
        "query A($input: FindInput) { discover { findOrder(input: $input) { title } } }",
        "*'$input' is FindInput, but*takes FindInput!*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { title @include(if: true) } } }",
        "*uses a directive*@include and @skip are not supported*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { title @skip(if: true) } } }",
        "*uses a directive*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) @defer { title } } }",
        "*uses a directive*"
    )]
    [TestCase(
        "query A { discover { search(input: { id: 1 }) @stream { __typename } } }",
        "*uses a directive*"
    )]
    [TestCase(
        "query A @cached { discover { findOrder(input: { id: 1 }) { title } } }",
        "*uses a directive*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { ... on Customer { name } } } }",
        "*'... on Customer' can never apply to a Order*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { t: title t: level } } }",
        "*'t' names two different selections under Order*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { customer { a: orderById(id: 1) { title } a: orderById(id: 2) { title } } } } }",
        "*'a' names two different selections under Customer*"
    )]
    [TestCase(
        "query A { discover { findOrder(input: { id: 1 }) { ...Missing } } }",
        "*no file defines the fragment 'Missing'*"
    )]
    [TestCase("type X { a: Int }", "*neither an operation nor a fragment*")]
    public void A_document_the_host_would_refuse_is_refused_naming_where(
        string text,
        string message
    )
    {
        var render = () => Render(("a.graphql", text));

        render.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [TestCase(
        "fragment Badge on Customer { name }",
        "query A { discover { findOrder(input: { id: 1 }) { ...Badge } } }",
        "*Badge' is on Customer, which can never apply to a Order*"
    )]
    [TestCase(
        "fragment X on Order { customer { ...Y } } fragment Y on Customer { orders { ...X } }",
        "query A { discover { findOrder(input: { id: 1 }) { ...X } } }",
        "*the fragment 'X' spreads itself (X > Y > X)*"
    )]
    [TestCase(
        "fragment Unused on Order { title }",
        "query A { discover { findOrder(input: { id: 1 }) { title } } }",
        "*the fragment 'Unused' is spread by no operation*"
    )]
    [TestCase(
        "fragment Badge on Customer { name } fragment Badge on Customer { name }",
        "query A { discover { findOrder(input: { id: 1 }) { customer { ...Badge } } } }",
        "*the fragment 'Badge' is defined twice*"
    )]
    [TestCase(
        "fragment Badge on Nothing { name }",
        "query A { discover { findOrder(input: { id: 1 }) { title } } }",
        "*'Badge' is on 'Nothing'*"
    )]
    public void A_fragment_the_host_would_refuse_is_refused(
        string fragments,
        string operation,
        string message
    )
    {
        var render = () => Render(("fragments.graphql", fragments), ("a.graphql", operation));

        render.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [TestCase("ConfigureTrax", "CustomerBadge", "*configureTrax*")]
    [TestCase("OrderCard", "OrderCardResult", "*OrderCardResult*")]
    [TestCase("OrderCard", "StartRun", "*")]
    public void A_name_another_part_of_the_client_uses_is_refused(
        string operation,
        string fragment,
        string message
    )
    {
        var render = () =>
            Render(
                ("fragments.graphql", $"fragment {fragment} on Customer {{ name }}"),
                (
                    "a.graphql",
                    $"query {operation}($input: FindInput!) {{ discover {{ findOrder(input: $input) {{ customer {{ ...{fragment} }} }} }} }}"
                )
            );

        if (fragment == "StartRun")
            render.Should().NotThrow("a fragment named like nothing else is fine");
        else
            render.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public void Two_operations_of_one_name_are_refused()
    {
        var render = () =>
            Render(
                ("a.graphql", "query Same { discover { findOrder(input: { id: 1 }) { title } } }"),
                ("b.graphql", "query Same { discover { findOrder(input: { id: 2 }) { title } } }")
            );

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*a.graphql and b.graphql: the operation name 'Same' is used twice*");
    }

    [Test]
    public void Every_refusal_is_reported_at_once()
    {
        var render = () =>
            Render(
                ("a.graphql", "query A { discover { findOrder(input: { id: 1 }) { colour } } }"),
                ("b.graphql", "query B { discover { findOrder(input: { id: 1 }) { shade } } }")
            );

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*a.graphql*colour*b.graphql*shade*");
    }

    #endregion

    #region The command

    [Test]
    public void The_command_reads_its_globs_and_check_reports_a_stale_documents_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-docs-cmd-{Guid.NewGuid():N}");
        var src = Path.Combine(dir, "src", "trax");
        Directory.CreateDirectory(Path.Combine(src, "cards"));
        var manifest = Path.Combine(dir, "manifest.json");
        File.WriteAllText(manifest, ClientManifest.Write(Manifest));
        File.WriteAllText(Path.Combine(src, "cards", "orderCard.graphql"), OrderCard);
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            ClientCommand
                .Run(
                    "react",
                    manifest,
                    src,
                    check: false,
                    null,
                    null,
                    new FakeNodeRunner(),
                    ["src/**/*.graphql"]
                )
                .Should()
                .Be(0);

            var json = File.ReadAllText(Path.Combine(src, "trax.documents.json"));
            json.Should().Contain("\"source\": \"src/trax/cards/orderCard.graphql\"");
            File.ReadAllText(Path.Combine(src, "graphql.config.yml"))
                .Should()
                .Contain("  - \"../**/*.graphql\"");
            File.Exists(Path.Combine(src, "documents", "orderCard.g.ts")).Should().BeTrue();
            ClientCommand
                .Run(
                    "react",
                    manifest,
                    src,
                    check: true,
                    null,
                    null,
                    new FakeNodeRunner(),
                    ["src/**/*.graphql"]
                )
                .Should()
                .Be(0);

            File.WriteAllText(
                Path.Combine(src, "cards", "orderCard.graphql"),
                OrderCard.Replace("title", "level")
            );
            ClientCommand
                .Run(
                    "react",
                    manifest,
                    src,
                    check: true,
                    null,
                    null,
                    new FakeNodeRunner(),
                    ["src/**/*.graphql"]
                )
                .Should()
                .Be(
                    1,
                    "the committed trax.documents.json and orderCard.g.ts no longer match the documents"
                );

            ClientCommand
                .Run(
                    "react",
                    manifest,
                    src,
                    check: false,
                    null,
                    null,
                    new FakeNodeRunner(),
                    ["nowhere/*.graphql"]
                )
                .Should()
                .Be(1, "a glob that matches nothing is an error");
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestCase(
        "src/**/*.graphql",
        new[] { "src/a.graphql", "src/x/b.graphql", "src/x/y/c.graphql" }
    )]
    [TestCase("src/*.graphql", new[] { "src/a.graphql" })]
    [TestCase("src/x/*.graphql", new[] { "src/x/b.graphql" })]
    [TestCase("src/x/b.graphql", new[] { "src/x/b.graphql" })]
    [TestCase("src/?.graphql", new[] { "src/a.graphql" })]
    public void A_glob_names_the_files_it_should(string pattern, string[] expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-glob-{Guid.NewGuid():N}");
        foreach (
            var file in new[]
            {
                "src/a.graphql",
                "src/x/b.graphql",
                "src/x/y/c.graphql",
                "src/a.ts",
            }
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file))!);
            File.WriteAllText(Path.Combine(dir, file), "");
        }
        try
        {
            Glob.Files(dir, pattern)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                .Should()
                .Equal(expected);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    #endregion

    #region Query models refused

    [TestCase(
        "mutation M { dispatch { orders { totalCount } } }",
        "*src/a.graphql:1:*'dispatch.orders' is a query model; a mutation cannot hold one*"
    )]
    [TestCase(
        "query Q($where: OrderFilterInput) { discover { orders(where: $where) { totalCount } } }",
        "*src/a.graphql:1:*'$where' as OrderFilterInput*filter or sort input*"
    )]
    [TestCase(
        "query Q($order: [OrderSortInput!]) { discover { orders(order: $order) { totalCount } } }",
        "*src/a.graphql:1:*'$order' as OrderSortInput*filter or sort input*"
    )]
    [TestCase(
        "query Q($title: StringOperationFilterInput) { discover { orders(where: { title: $title }) { totalCount } } }",
        "*src/a.graphql:1:*'$title'*filter or sort input*"
    )]
    [TestCase(
        "query Q { discover { runs { ledger(first: 2) { title } } } }",
        "*src/a.graphql:1:*the query model 'ledger' takes no argument 'first'*"
    )]
    public void A_query_model_document_the_host_would_refuse_is_refused(string text, string message)
    {
        var render = () => Render(("src/a.graphql", text));

        render.Should().Throw<InvalidOperationException>().WithMessage(message, "per " + Adr);
    }

    [TestCase("query Q { discover { orders(where: { colour: { eq: \"x\" } }) { totalCount } } }")]
    [TestCase("query Q { discover { orders(where: { title: { eq: 5 } }) { totalCount } } }")]
    [TestCase("query Q { discover { orders(order: [{ colour: ASC }]) { totalCount } } }")]
    public void An_inline_filter_or_sort_the_types_do_not_hold_is_refused(string text)
    {
        var render = () => Render(("src/a.graphql", text));

        render.Should().Throw<InvalidOperationException>().WithMessage("*src/a.graphql:1:*");
    }

    #endregion

    #region Helpers

    // A train as the manifest lists it, its arguments read from the variables a document of it would declare.
    private static ClientManifestTrain Train(
        string root,
        string? ns,
        string field,
        string answer,
        string document
    ) =>
        new()
        {
            Field = field,
            Root = root,
            Namespace = ns,
            Answer = answer,
            Arguments = GraphQLParser
                .Parser.Parse(document)
                .Definitions.OfType<GraphQLParser.AST.GraphQLOperationDefinition>()
                .Single()
                .Variables!.Items.Select(v => new ClientManifestArgument
                {
                    Name = v.Variable.Name.StringValue,
                    Type = PrintType(v.Type),
                })
                .ToList(),
        };

    private static string PrintType(GraphQLParser.AST.GraphQLType type) =>
        type switch
        {
            GraphQLParser.AST.GraphQLNonNullType n => PrintType(n.Type) + "!",
            GraphQLParser.AST.GraphQLListType l => "[" + PrintType(l.Type) + "]",
            GraphQLParser.AST.GraphQLNamedType named => named.Name.StringValue,
            _ => "",
        };

    private static IReadOnlyList<ClientFile> Render(
        params (string Source, string Text)[] documents
    ) =>
        new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(
                Manifest,
                Documents: documents.Select(d => new ClientDocumentFile(d.Source, d.Text)).ToList()
            )
        );

    private static string Text(IReadOnlyList<ClientFile> files, string path) =>
        files.Single(f => f.Path == path).Text;

    private static JsonElement Json(IReadOnlyList<ClientFile> files) =>
        JsonDocument.Parse(Text(files, "trax.documents.json")).RootElement;

    private static string Id(IReadOnlyList<ClientFile> files)
    {
        var text = Json(files).GetProperty("documents")[0].GetProperty("text").GetString()!;
        return ClientDocumentSet.IdOf(
            Json(files).GetProperty("documents")[0].GetProperty("name").GetString()!,
            text
        );
    }

    private static IEnumerable<(string Name, string Text, string Id)> FixtureCases()
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
        Assert.Ignore("Trax.Api's client-document-ids.json not found (isolated build).");
        return [];
    }

    #endregion
}
