using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Tests.Fakes;
using Trax.StateMachine;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A client document's function answers the type of what it selects, not the type the train's answer has in the schema: a
/// field the document leaves out is not in it, nullability and lists follow the SDL, a union or interface is one
/// object per possible type told apart by <c>__typename</c>, and an enum is the one <c>types.g.ts</c> declares, which
/// holds only what a client sends.
/// </summary>
public class SelectionTypesTests
{
    private const string Types = """
        enum Level {
          LOW
          HIGH
        }

        input FindInput {
          id: Int!
        }

        type Order {
          title: String!
          level: Level
          customer: Customer!
        }

        type Customer {
          "The customer's name."
          name: String!
          orders: [Order!]!
        }

        type Shapes {
          strict: [Int!]!
          loose: [Int]!
          maybe: [Int!]
          note: String
          box: Box
        }

        type Box {
          size: Int!
        }

        union Found = Order | Customer

        interface Named {
          name: String!
        }

        type Person implements Named {
          name: String!
          age: Int!
        }

        type Pet implements Named {
          name: String!
        }

        type Empty {
          id: Int
        }
        """;

    [Test]
    public async Task A_trains_function_returns_exactly_what_its_document_selects()
    {
        var train = await TrainAsync(
                "Order!",
                "findOrder(input: $input) { title customer { name } }"
            )
            .ConfigureAwait(false);

        train
            .Should()
            .Contain("Promise<FindOrderResult>")
            .And.Contain(
                """
                export type FindOrderResult = {
                  title: string;
                  customer: {
                    /** The customer's name. */
                    name: string;
                  };
                };
                """
            )
            .And.NotContain("orders", "the document leaves the back-reference out");
    }

    [Test]
    public async Task No_output_type_is_declared_per_model()
    {
        var files = await RenderAsync("Order!", "findOrder(input: $input) { title }")
            .ConfigureAwait(false);
        var types = files
            .Single(f => string.Equals(f.Path, "types.g.ts", StringComparison.Ordinal))
            .Text;

        types
            .Should()
            .Contain("export type Level = \"LOW\" | \"HIGH\";")
            .And.Contain("export interface FindInput {");
        foreach (
            var output in new[] { "Order", "Customer", "Shapes", "Box", "Found", "Named", "Person" }
        )
            types
                .Should()
                .NotMatchRegex(
                    $@"export (interface|type) {output}\b",
                    "an answer is typed per document, never per model"
                );
    }

    [Test]
    public async Task Nullability_and_lists_follow_the_sdl()
    {
        var train = await TrainAsync(
                "Shapes!",
                "findOrder(input: $input) { strict loose maybe note box { size } }"
            )
            .ConfigureAwait(false);

        train
            .Should()
            .Contain("  strict: Array<number>;")
            .And.Contain("  loose: Array<number | null>;")
            .And.Contain("  maybe: Array<number> | null;")
            .And.Contain("  note: string | null;")
            .And.Contain(
                """
                  box: {
                    size: number;
                  } | null;
                """
            );
    }

    [Test]
    public async Task A_nullable_list_answer_keeps_both_nulls()
    {
        (await TrainAsync("[Box]", "findOrder(input: $input) { size }").ConfigureAwait(false))
            .Should()
            .Contain(
                """
                export type FindOrderResult = Array<{
                  size: number;
                } | null> | null;
                """
            );
    }

    [Test]
    public async Task A_union_answer_is_one_object_per_member_told_apart_by_typename()
    {
        var train = await TrainAsync(
                "Found!",
                "findOrder(input: $input) { __typename ... on Order { title } ... on Customer { name } }"
            )
            .ConfigureAwait(false);

        train
            .Should()
            .Contain(
                """
                export type FindOrderResult = {
                  __typename: "Customer";
                  /** The customer's name. */
                  name: string;
                } | {
                  __typename: "Order";
                  title: string;
                };
                """
            );
    }

    [Test]
    public async Task A_union_member_the_document_leaves_out_still_arrives_as_its_typename()
    {
        (
            await TrainAsync(
                    "Found!",
                    "findOrder(input: $input) { __typename ... on Order { title } }"
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Contain(
                """
                export type FindOrderResult = {
                  __typename: "Customer";
                } | {
                """,
                "a value of a member with no fragment still arrives, with only its __typename"
            );
    }

    [Test]
    public async Task An_interface_answer_is_one_object_per_implementation()
    {
        (
            await TrainAsync(
                    "Named!",
                    "findOrder(input: $input) { __typename name ... on Person { age } }"
                )
                .ConfigureAwait(false)
        )
            .Should()
            .Contain(
                """
                export type FindOrderResult = {
                  __typename: "Person";
                  name: string;
                  age: number;
                } | {
                  __typename: "Pet";
                  name: string;
                };
                """
            );
    }

    [Test]
    public async Task An_object_the_document_selects_only_by_typename_is_its_typename()
    {
        (
            await TrainAsync("Empty!", "findOrder(input: $input) { __typename }")
                .ConfigureAwait(false)
        )
            .Should()
            .Contain(
                """
                export type FindOrderResult = {
                  __typename: "Empty";
                };
                """
            );
    }

    [Test]
    public async Task An_enum_in_an_answer_is_imported_from_the_types_a_client_sends()
    {
        var train = await TrainAsync("Order!", "findOrder(input: $input) { level }")
            .ConfigureAwait(false);

        train
            .Should()
            .Contain("import type { FindInput, Level } from \"../types.g\";")
            .And.Contain("  level: Level | null;");
    }

    [Test]
    public async Task An_alias_names_its_key()
    {
        (
            await TrainAsync("Order!", "findOrder(input: $input) { heading: title }")
                .ConfigureAwait(false)
        )
            .Should()
            .Contain("  heading: string;")
            .And.NotContain("  title:");
    }

    [Test]
    public async Task A_document_selecting_a_field_the_types_do_not_hold_is_refused()
    {
        var render = async () =>
            await RenderAsync("Order!", "findOrder(input: $input) { colour }")
                .ConfigureAwait(false);

        (
            await render.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*src/findOrder.graphql*Order has no field 'colour'*");
    }

    [Test]
    public async Task The_progress_module_types_runProgress_from_its_documents()
    {
        var files = (
            await new ClientGenerator(new FakeNodeRunner())
                .RenderAsync(
                    new ClientGenerateOptions(ClientManifests.Sample() with { Machines = [] })
                )
                .ConfigureAwait(false)
        );
        var progress = files
            .Single(f => string.Equals(f.Path, "progress.g.ts", StringComparison.Ordinal))
            .Text;

        progress
            .Should()
            .Contain("import type { RunProgressState } from \"./types.g\";")
            .And.Contain("export type RunProgress = {")
            .And.Contain("  state: RunProgressState | null;")
            .And.Contain("export type RunProgressEvent = {")
            .And.Contain("  sequence: number;");
        files
            .Single(f => string.Equals(f.Path, "index.g.ts", StringComparison.Ordinal))
            .Text.Should()
            .Contain("type RunProgress, type RunProgressEvent, type RunProgressView");
        files
            .Single(f => string.Equals(f.Path, "types.g.ts", StringComparison.Ordinal))
            .Text.Should()
            .NotContain("JunctionStep", "an answer's object types are not declared per model");
    }

    private static async Task<string> TrainAsync(string answer, string selection) =>
        (await RenderAsync(answer, selection).ConfigureAwait(false))
            .Single(f =>
                string.Equals(f.Path, "documents/findOrder.g.ts", StringComparison.Ordinal)
            )
            .Text;

    private static async Task<IReadOnlyList<ClientFile>> RenderAsync(
        string answer,
        string selection
    ) =>
        (
            await new ClientGenerator(new FakeNodeRunner())
                .RenderAsync(
                    new ClientGenerateOptions(
                        new ClientManifest
                        {
                            Trains =
                            [
                                new ClientManifestTrain
                                {
                                    Field = "findOrder",
                                    Root = "query",
                                    Answer = answer,
                                    Arguments =
                                    [
                                        new ClientManifestArgument
                                        {
                                            Name = "input",
                                            Type = "FindInput!",
                                        },
                                    ],
                                },
                            ],
                            Types = Types,
                        },
                        Documents:
                        [
                            new ClientDocumentFile(
                                "src/findOrder.graphql",
                                "query FindOrder($input: FindInput!) { discover { "
                                    + selection
                                    + " } }"
                            ),
                        ]
                    )
                )
                .ConfigureAwait(false)
        );
}
