using System.Text.Json;
using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Commands;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Core.Extensions;
using Trax.StateMachine;

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
public partial class ClientDocumentsGeneratorTests
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
                ns: null,
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
                ns: null,
                "search",
                "[Found!]!",
                "query Search($input: FindInput!) { discover { search(input: $input) { __typename } } }"
            ),
            Train(
                "mutation",
                ns: null,
                "recordNote",
                "NoteResponse!",
                "mutation RecordNote($input: NoteInput!, $priority: Int) { dispatch { recordNote(input: $input, priority: $priority) { externalId } } }"
            ),
            Train(
                "mutation",
                ns: null,
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
    public async Task A_document_selecting_a_query_model_returns_exactly_that()
    {
        var files = await RenderAsync(
                (
                    "src/orderList.graphql",
                    "query OrderList($level: Level, $after: String) { discover { orders(first: 2, after: $after, "
                        + "where: { level: { eq: $level } }, order: [{ title: ASC }]) { totalCount nodes { title } } } }"
                )
            )
            .ConfigureAwait(false);
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
    public async Task A_query_with_a_train_and_a_query_model_answers_both()
    {
        var module = Text(
            await RenderAsync(
                    (
                        "src/both.graphql",
                        "query Both($input: FindInput!) { discover { findOrder(input: $input) { title } "
                            + "orders { totalCount } runs { ledger { title } } } }"
                    )
                )
                .ConfigureAwait(false),
            "documents/both.g.ts"
        );

        module
            .Should()
            .Contain("findOrder: {")
            .And.Contain("orders: {")
            .And.Contain("ledger: Array<{");
    }

    // A variable-less document's module writes Record<string, never> beside the types it imports: a GraphQL type named
    // Record would hide the global one, and the module would not compile.
    [TestCase("Record")]
    [TestCase("Readonly")]
    [TestCase("Partial")]
    // TypeScript's primitive types cannot name a type alias at all.
    [TestCase("bigint")]
    [TestCase("symbol")]
    [TestCase("unknown")]
    [TestCase("never")]
    [TestCase("object")]
    [TestCase("any")]
    [TestCase("number")]
    [TestCase("string")]
    [TestCase("boolean")]
    [TestCase("undefined")]
    // The type operators: a field typed `keyof` is the start of a type, not a type.
    [TestCase("keyof")]
    [TestCase("readonly")]
    [TestCase("unique")]
    [TestCase("infer")]
    public async Task A_type_named_as_a_global_generic_is_refused(string name)
    {
        var manifest = Manifest with { Types = LevelWord.Replace(Types, name) };

        var render = async () =>
            (
                await new ClientGenerator(new FakeNodeRunner())
                    .RenderAsync(
                        new ClientGenerateOptions(
                            manifest,
                            Documents:
                            [
                                new ClientDocumentFile(
                                    "src/shop.graphql",
                                    "query Shop { discover { shop { catalog { level } } } }"
                                ),
                            ]
                        )
                    )
                    .ConfigureAwait(false)
            );

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage($"*'{name}'*reserves*");
    }

    [Test]
    public async Task A_namespace_holding_only_query_models_resolves()
    {
        var render = async () =>
            await RenderAsync(
                    ("src/shop.graphql", "query Shop { discover { shop { catalog { title } } } }")
                )
                .ConfigureAwait(false);

        await render
            .Should()
            .NotThrowAsync("a namespace counts when a query model is in it")
            .ConfigureAwait(false);
    }

    [Test]
    public async Task The_editor_schema_holds_the_query_models_under_their_namespaces()
    {
        var schema = Text(
            await RenderAsync(("src/orderCard.graphql", OrderCard)).ConfigureAwait(false),
            "schema.graphql"
        );

        schema
            .Should()
            .Contain(
                "orders(first: Int, after: String, where: OrderFilterInput, order: [OrderSortInput!]): OrdersConnection"
            )
            .And.Contain("ledger: [Order!]!")
            .And.Contain("catalog: [Order!]!");
    }

    [Test]
    public async Task A_document_selecting_a_subset_returns_exactly_that()
    {
        var files = await RenderAsync(("src/trax/orderCard.graphql", OrderCard))
            .ConfigureAwait(false);
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
        var text = entry.GetProperty("text").GetString().NotNull();
        module
            .Should()
            .Contain(
                $"export const orderCardDocumentId = \"{ClientDocumentSet.IdOf("OrderCard", text)}\";",
                "the function sends the id the host gives the text it registers"
            );
        json.GetProperty("format").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task The_cli_and_the_host_hash_a_document_alike()
    {
        foreach (var (name, text, id) in (await FixtureCasesAsync().ConfigureAwait(false)))
            ClientDocumentSet.IdOf(name, text).Should().Be(id, $"the fixture's id for {name}");
    }

    [Test]
    public async Task Reformatting_a_document_does_not_move_its_id_and_changing_a_field_does()
    {
        var compact =
            "query OrderCard($input: FindInput!) { discover { findOrder(input: $input) { title customer { name } } } }";
        var commented =
            "# the order card\n"
            + OrderCard.Replace("title", "title # heading", StringComparison.Ordinal);
        var changed = compact.Replace("title", "level", StringComparison.Ordinal);

        var a = Id(await RenderAsync(("a.graphql", compact)).ConfigureAwait(false));
        Id(await RenderAsync(("a.graphql", OrderCard)).ConfigureAwait(false)).Should().Be(a);
        Id(await RenderAsync(("a.graphql", commented)).ConfigureAwait(false)).Should().Be(a);
        Id(await RenderAsync(("a.graphql", changed)).ConfigureAwait(false)).Should().NotBe(a);
    }

    [Test]
    public async Task A_query_of_two_trains_answers_both_under_their_keys()
    {
        var module = Text(
            await RenderAsync(
                    (
                        "two.graphql",
                        "query Two($input: FindInput!) { discover { findOrder(input: $input) { title } "
                            + "runs { findRun(input: $input) { level } } } }"
                    )
                )
                .ConfigureAwait(false),
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
    public async Task Two_aliases_of_one_field_are_two_keys()
    {
        var module = Text(
            await RenderAsync(
                    (
                        "pair.graphql",
                        "query Pair($a: FindInput!, $b: FindInput!) { discover { first: findOrder(input: $a) { title } "
                            + "second: findOrder(input: $b) { heading: title } } }"
                    )
                )
                .ConfigureAwait(false),
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
    public async Task A_fragment_in_another_file_is_carried_by_every_document_that_spreads_it()
    {
        var files = await RenderAsync(
                ("fragments/badge.graphql", "fragment CustomerBadge on Customer { name }"),
                (
                    "a.graphql",
                    "query A($input: FindInput!) { discover { findOrder(input: $input) { title customer { ...CustomerBadge } } } }"
                ),
                (
                    "b.graphql",
                    "query B($input: FindInput!) { discover { runs { findRun(input: $input) { customer { ...CustomerBadge orders { title } } } } } }"
                )
            )
            .ConfigureAwait(false);

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
    public async Task An_inline_fragment_on_a_union_member_types_that_member()
    {
        Text(
                await RenderAsync(
                        (
                            "s.graphql",
                            "query S($input: FindInput!) { discover { search(input: $input) { __typename ... on Order { title } } } }"
                        )
                    )
                    .ConfigureAwait(false),
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
    public async Task A_back_reference_may_be_selected_explicitly_paged_or_not()
    {
        var module = Text(
            await RenderAsync(
                    (
                        "deep.graphql",
                        "query Deep($input: FindInput!, $first: Int) { discover { findOrder(input: $input) { customer { "
                            + "orders { title } ordersPage(first: $first) { nodes { title } pageInfo { hasNextPage } } "
                            + "orderById(id: 3) { title } } } } }"
                    )
                )
                .ConfigureAwait(false),
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
    public async Task A_required_argument_may_be_a_variable()
    {
        Text(
                await RenderAsync(
                        (
                            "byId.graphql",
                            "query ById($input: FindInput!, $id: Int!) { discover { findOrder(input: $input) { customer { orderById(id: $id) { title } } } } }"
                        )
                    )
                    .ConfigureAwait(false),
                "documents/byId.g.ts"
            )
            .Should()
            .Contain("export function byId(input: FindInput, id: number): Promise<ByIdResult>");
    }

    [Test]
    public async Task A_mutation_sends_its_train_and_optional_variables_with_their_defaults()
    {
        Text(
                await RenderAsync(
                        (
                            "note.graphql",
                            "mutation Note($input: NoteInput!, $priority: Int = 2) { dispatch { recordNote(input: $input, priority: $priority) { output { text } } } }"
                        )
                    )
                    .ConfigureAwait(false),
                "documents/note.g.ts"
            )
            .Should()
            .Contain(
                "export function note(input: NoteInput, options: { priority?: number | null } = {})"
            )
            .And.Contain("[\"dispatch\", \"recordNote\"]");
    }

    [Test]
    public async Task The_editor_gets_a_schema_holding_the_trains_and_a_config_naming_the_globs()
    {
        var files = (
            await new ClientGenerator(new FakeNodeRunner())
                .RenderAsync(
                    new ClientGenerateOptions(
                        Manifest,
                        Documents: [new ClientDocumentFile("src/a.graphql", OrderCard)],
                        DocumentGlobs: ["../**/*.graphql"]
                    )
                )
                .ConfigureAwait(false)
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
    public async Task One_input_writes_the_same_bytes()
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

        (await RenderAsync(documents).ConfigureAwait(false))
            .Should()
            .BeEquivalentTo(
                await RenderAsync([.. documents.Reverse()]).ConfigureAwait(false),
                o => o.WithStrictOrdering()
            );
    }

    [Test]
    public async Task Without_documents_none_of_their_files_are_written()
    {
        var files = (
            await new ClientGenerator(new FakeNodeRunner())
                .RenderAsync(new ClientGenerateOptions(Manifest))
                .ConfigureAwait(false)
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

    [Test]
    public async Task Generating_without_documents_removes_and_check_reports_the_files_a_run_with_them_wrote()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-docs-stale-{Guid.NewGuid():N}");
        var generator = new ClientGenerator(new FakeNodeRunner());
        var with = new ClientGenerateOptions(
            Manifest,
            Documents: [new ClientDocumentFile("cards/orderCard.graphql", OrderCard)]
        );
        var without = new ClientGenerateOptions(Manifest);
        try
        {
            await generator.WriteAsync(with, dir).ConfigureAwait(false);
            string[] written = ["trax.documents.json", "schema.graphql", "graphql.config.yml"];
            foreach (var name in written)
                File.Exists(Path.Combine(dir, name)).Should().BeTrue();

            (await generator.CheckAsync(without, dir).ConfigureAwait(false))
                .Should()
                .Contain(written.Select(name => new ClientDrift(name, DriftStatus.Drifted)));

            await generator.WriteAsync(without, dir).ConfigureAwait(false);
            foreach (var name in written)
                File.Exists(Path.Combine(dir, name)).Should().BeFalse(name);
            (await generator.CheckAsync(without, dir).ConfigureAwait(false)).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Generating_without_documents_keeps_files_of_those_names_it_did_not_write()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-docs-own-{Guid.NewGuid():N}");
        var generator = new ClientGenerator(new FakeNodeRunner());
        var without = new ClientGenerateOptions(Manifest);
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dir, "trax.documents.json"),
                "{\"format\": 1}\n"
            );
            await File.WriteAllTextAsync(
                Path.Combine(dir, "schema.graphql"),
                "type Query { a: Int }\n"
            );
            await File.WriteAllTextAsync(
                Path.Combine(dir, "graphql.config.yml"),
                "schema: x.graphql\n"
            );

            string[] own = ["trax.documents.json", "schema.graphql", "graphql.config.yml"];
            (await generator.CheckAsync(without, dir).ConfigureAwait(false))
                .Select(d => d.Path)
                .Should()
                .NotContain(own);
            await generator.WriteAsync(without, dir).ConfigureAwait(false);

            foreach (var name in own)
                File.Exists(Path.Combine(dir, name)).Should().BeTrue(name);
            (await generator.CheckAsync(without, dir).ConfigureAwait(false)).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
    [TestCase(
        "query A($input: FindInput!, $input: FindInput!) { discover { findOrder(input: $input) { title } } }",
        "*a.graphql:1:29: 'A' declares the variable '$input' more than once.*"
    )]
    [TestCase("type X { a: Int }", "*neither an operation nor a fragment*")]
    public async Task A_document_the_host_would_refuse_is_refused_naming_where(
        string text,
        string message
    )
    {
        var render = async () => await RenderAsync(("a.graphql", text)).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage(message);
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
    public async Task A_fragment_the_host_would_refuse_is_refused(
        string fragments,
        string operation,
        string message
    )
    {
        var render = async () =>
            await RenderAsync(("fragments.graphql", fragments), ("a.graphql", operation))
                .ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage(message);
    }

    [TestCase("ConfigureTrax", "CustomerBadge", "*configureTrax*")]
    [TestCase("OrderCard", "OrderCardResult", "*OrderCardResult*")]
    [TestCase("OrderCard", "StartRun", "*")]
    public async Task A_name_another_part_of_the_client_uses_is_refused(
        string operation,
        string fragment,
        string message
    )
    {
        var render = async () =>
            await RenderAsync(
                    ("fragments.graphql", $"fragment {fragment} on Customer {{ name }}"),
                    (
                        "a.graphql",
                        $"query {operation}($input: FindInput!) {{ discover {{ findOrder(input: $input) {{ customer {{ ...{fragment} }} }} }} }}"
                    )
                )
                .ConfigureAwait(false);

        if (string.Equals(fragment, "StartRun", StringComparison.Ordinal))
            await render
                .Should()
                .NotThrowAsync("a fragment named like nothing else is fine")
                .ConfigureAwait(false);
        else
            (
                await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
            ).WithMessage(message);
    }

    [Test]
    public async Task Two_operations_of_one_name_are_refused()
    {
        var render = async () =>
            await RenderAsync(
                    (
                        "a.graphql",
                        "query Same { discover { findOrder(input: { id: 1 }) { title } } }"
                    ),
                    (
                        "b.graphql",
                        "query Same { discover { findOrder(input: { id: 2 }) { title } } }"
                    )
                )
                .ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*a.graphql and b.graphql: the operation name 'Same' is used twice*");
    }

    [Test]
    public async Task Every_refusal_is_reported_at_once()
    {
        var render = async () =>
            await RenderAsync(
                    (
                        "a.graphql",
                        "query A { discover { findOrder(input: { id: 1 }) { colour } } }"
                    ),
                    ("b.graphql", "query B { discover { findOrder(input: { id: 1 }) { shade } } }")
                )
                .ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*a.graphql*colour*b.graphql*shade*");
    }

    #endregion

    #region The command

    [Test]
    public async Task The_command_reads_its_globs_and_check_reports_a_stale_documents_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-docs-cmd-{Guid.NewGuid():N}");
        var src = Path.Combine(dir, "src", "trax");
        Directory.CreateDirectory(Path.Combine(src, "cards"));
        var manifest = Path.Combine(dir, "manifest.json");
        await File.WriteAllTextAsync(manifest, ClientManifest.Write(Manifest));
        await File.WriteAllTextAsync(Path.Combine(src, "cards", "orderCard.graphql"), OrderCard);
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            (
                await RunReactClientAsync(manifest, src, check: false, "src/**/*.graphql")
                    .ConfigureAwait(false)
            )
                .Should()
                .Be(0);

            await AssertWroteDocumentsAsync(src).ConfigureAwait(false);
            (
                await RunReactClientAsync(manifest, src, check: true, "src/**/*.graphql")
                    .ConfigureAwait(false)
            )
                .Should()
                .Be(0);
            await File.WriteAllTextAsync(
                    Path.Combine(src, "cards", "orderCard.graphql"),
                    OrderCard.Replace("title", "level", StringComparison.Ordinal)
                )
                .ConfigureAwait(false);
            (
                await RunReactClientAsync(manifest, src, check: true, "src/**/*.graphql")
                    .ConfigureAwait(false)
            )
                .Should()
                .Be(
                    1,
                    "the committed trax.documents.json and orderCard.g.ts no longer match the documents"
                );

            (
                await RunReactClientAsync(manifest, src, check: false, "nowhere/*.graphql")
                    .ConfigureAwait(false)
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

    // What a run over the glob writes under --out: the documents file naming each document by its source, the editor's
    // config, and each document's module.
    private static async Task AssertWroteDocumentsAsync(string src)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(src, "trax.documents.json"))
            .ConfigureAwait(false);
        json.Should().Contain("\"source\": \"src/trax/cards/orderCard.graphql\"");
        (await File.ReadAllTextAsync(Path.Combine(src, "graphql.config.yml")).ConfigureAwait(false))
            .Should()
            .Contain("  - \"../**/*.graphql\"");
        File.Exists(Path.Combine(src, "documents", "orderCard.g.ts")).Should().BeTrue();
    }

    // Runs `trax client react` over the glob with a fake node, writing nothing to the console.
    private static Task<int> RunReactClientAsync(
        string manifest,
        string src,
        bool check,
        string glob
    ) =>
        ClientCommand.RunAsync(
            "react",
            manifest,
            src,
            check: check,
            specifier: null,
            toolsDir: null,
            new FakeNodeRunner(),
            TextWriter.Null,
            TextWriter.Null,
            [glob]
        );

    // The schema written for the editor sits under --out, where the glob matches it too. Named through a link, --out
    // must still be the folder the glob finds it in, or the second run reads the schema as a document.
    [Test]
    public async Task The_command_never_reads_its_editor_schema_when_out_is_named_through_a_link()
    {
        var real = Directory.CreateTempSubdirectory("trax-docs-real-").FullName;
        var link = Path.Combine(Path.GetTempPath(), $"trax-docs-link-{Guid.NewGuid():N}");
        var src = Path.Combine(real, "src", "trax");
        Directory.CreateDirectory(Path.Combine(src, "cards"));
        Directory.CreateSymbolicLink(link, real);
        var manifest = Path.Combine(real, "manifest.json");
        await File.WriteAllTextAsync(manifest, ClientManifest.Write(Manifest));
        await File.WriteAllTextAsync(Path.Combine(src, "cards", "orderCard.graphql"), OrderCard);
        var outDir = Path.Combine(link, "src", "trax");
        var previous = Directory.GetCurrentDirectory();
        async Task<int> RunAsync(bool check) =>
            await ClientCommand
                .RunAsync(
                    "react",
                    manifest,
                    outDir,
                    check,
                    specifier: null,
                    toolsDir: null,
                    new FakeNodeRunner(),
                    TextWriter.Null,
                    TextWriter.Null,
                    ["src/**/*.graphql"]
                )
                .ConfigureAwait(false);
        try
        {
            Directory.SetCurrentDirectory(real);
            (await RunAsync(check: false).ConfigureAwait(false)).Should().Be(0);
            File.Exists(Path.Combine(src, ClientGenerator.EditorSchemaFileName)).Should().BeTrue();

            (await RunAsync(check: false).ConfigureAwait(false))
                .Should()
                .Be(0, "the editor schema is not a document");
            (await RunAsync(check: true).ConfigureAwait(false)).Should().Be(0);
            (
                await File.ReadAllTextAsync(Path.Combine(src, "trax.documents.json"))
                    .ConfigureAwait(false)
            )
                .Should()
                .NotContain(ClientGenerator.EditorSchemaFileName);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(link);
            Directory.Delete(real, recursive: true);
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
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file)).NotNull());
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

    [Test]
    public void An_absolute_glob_names_the_files_where_it_points_and_the_editor_is_told_so()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-glob-{Guid.NewGuid():N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"trax-glob-cwd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "src"));
        Directory.CreateDirectory(cwd);
        File.WriteAllText(Path.Combine(dir, "src", "a.graphql"), "");
        var pattern = Path.Combine(dir, "src").Replace('\\', '/') + "/*.graphql";
        try
        {
            Glob.Files(cwd, pattern).Should().Equal(Path.Combine(dir, "src", "a.graphql"));
            Glob.RelativeTo(cwd, Path.Combine(cwd, "out"), pattern)
                .Should()
                .Be(
                    Path.GetRelativePath(Path.Combine(cwd, "out"), Path.Combine(dir, "src"))
                        .Replace('\\', '/') + "/*.graphql",
                    "graphql.config.yml names the folder the glob points at, not one under the working directory"
                );
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(cwd, recursive: true);
        }
    }

    // The working directory is always reported with its links resolved (macOS's temp folder is /var/..., a link to
    // /private/var/...), so --out named through a link must not read as a folder somewhere else.
    [Test]
    public void An_out_folder_named_through_a_link_gets_the_glob_relative_to_where_it_is()
    {
        var real = Directory.CreateTempSubdirectory("trax-glob-real-").FullName;
        var link = Path.Combine(Path.GetTempPath(), $"trax-glob-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(real, "src", "trax"));
        Directory.CreateSymbolicLink(link, real);
        try
        {
            Glob.RelativeTo(real, Path.Combine(link, "src", "trax"), "src/**/*.graphql")
                .Should()
                .Be("../**/*.graphql");
            Glob.Resolved(Path.Combine(link, "src", "trax", "schema.graphql"))
                .Should()
                .Be(Glob.Resolved(Path.Combine(real, "src", "trax", "schema.graphql")));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(real, recursive: true);
        }
    }

    // A case-insensitive file system answers to any spelling of a path, and the working directory and the globs'
    // matches carry the one on disk, so a path given in another case must resolve to that one.
    [Test]
    [Platform("MacOsX,Win")]
    public void A_path_spelled_in_another_case_resolves_to_its_spelling_on_disk()
    {
        var real = Directory.CreateTempSubdirectory("trax-glob-case-").FullName;
        Directory.CreateDirectory(Path.Combine(real, "src", "trax"));
        File.WriteAllText(Path.Combine(real, "src", "trax", "schema.graphql"), "");
        try
        {
            Glob.Resolved(Path.Combine(real, "Src", "Trax", "Schema.graphql"))
                .Should()
                .Be(Glob.Resolved(Path.Combine(real, "src", "trax", "schema.graphql")));
        }
        finally
        {
            Directory.Delete(real, recursive: true);
        }
    }

    // A drive-relative path (C:x) is read against the drive's current folder, not the root of C:.
    [Test]
    [Platform("Win")]
    public void A_glob_with_a_drive_is_read_from_that_drives_root()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-glob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.graphql"), "");
        try
        {
            Glob.Files(Path.GetTempPath(), Path.Combine(dir, "*.graphql"))
                .Should()
                .Equal(Path.Combine(dir, "a.graphql"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // `$` would also match before a newline that ends the name.
    [Test]
    [Platform(Exclude = "Win")]
    public void A_glob_does_not_match_a_name_with_more_after_a_newline()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-glob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.graphql"), "");
        File.WriteAllText(Path.Combine(dir, "b.graphql\n"), "");
        try
        {
            Glob.Files(dir, "*.graphql").Should().Equal(Path.Combine(dir, "a.graphql"));
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
    public async Task A_query_model_document_the_host_would_refuse_is_refused(
        string text,
        string message
    )
    {
        var render = async () => await RenderAsync(("src/a.graphql", text)).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage(message, "per " + Adr);
    }

    [TestCase("query Q { discover { orders(where: { colour: { eq: \"x\" } }) { totalCount } } }")]
    [TestCase("query Q { discover { orders(where: { title: { eq: 5 } }) { totalCount } } }")]
    [TestCase("query Q { discover { orders(order: [{ colour: ASC }]) { totalCount } } }")]
    public async Task An_inline_filter_or_sort_the_types_do_not_hold_is_refused(string text)
    {
        var render = async () => await RenderAsync(("src/a.graphql", text)).ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*src/a.graphql:1:*");
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
                .Variables.NotNull()
                .Items.Select(v => new ClientManifestArgument
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

    private static async Task<IReadOnlyList<ClientFile>> RenderAsync(
        params (string Source, string Text)[] documents
    ) =>
        (
            await new ClientGenerator(new FakeNodeRunner())
                .RenderAsync(
                    new ClientGenerateOptions(
                        Manifest,
                        Documents: documents
                            .Select(d => new ClientDocumentFile(d.Source, d.Text))
                            .ToList()
                    )
                )
                .ConfigureAwait(false)
        );

    private static string Text(IReadOnlyList<ClientFile> files, string path) =>
        files.Single(f => string.Equals(f.Path, path, StringComparison.Ordinal)).Text;

    private static JsonElement Json(IReadOnlyList<ClientFile> files) =>
        JsonDocument.Parse(Text(files, "trax.documents.json")).RootElement;

    private static string Id(IReadOnlyList<ClientFile> files)
    {
        var text = Json(files)
            .GetProperty("documents")[0]
            .GetProperty("text")
            .GetString()
            .NotNull();
        return ClientDocumentSet.IdOf(
            Json(files).GetProperty("documents")[0].GetProperty("name").GetString().NotNull(),
            text
        );
    }

    private static async Task<
        IEnumerable<(string Name, string Text, string Id)>
    > FixtureCasesAsync()
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
            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(candidate).ConfigureAwait(false)
            );
            return json
                .RootElement.GetProperty("cases")
                .EnumerateArray()
                .Select(c =>
                    (
                        c.GetProperty("name").GetString().NotNull(),
                        c.GetProperty("text").GetString().NotNull(),
                        c.GetProperty("id").GetString().NotNull()
                    )
                )
                .ToList();
        }
        Assert.Ignore("Trax.Api's client-document-ids.json not found (isolated build).");
        return [];
    }

    #endregion

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\bLevel\b",
        System.Text.RegularExpressions.RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial System.Text.RegularExpressions.Regex LevelWord { get; }
}
