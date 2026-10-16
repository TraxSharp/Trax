using AwesomeAssertions;
using Trax.Cli.Clients;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A train's function answers the type of what its document selects, not the type its answer has in the schema: a
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

        type Alert {
          title: String!
          level: Level
          campaign: Campaign!
        }

        type Campaign {
          "The campaign's name."
          name: String!
          alerts: [Alert!]!
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

        union Found = Alert | Campaign

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
    public void A_trains_function_returns_exactly_what_its_document_selects()
    {
        var train = Train("Alert!", "findAlert(input: $input) { title campaign { name } }");

        train
            .Should()
            .Contain("Promise<FindAlertResult>")
            .And.Contain(
                """
                export type FindAlertResult = {
                  title: string;
                  campaign: {
                    /** The campaign's name. */
                    name: string;
                  };
                };
                """
            )
            .And.NotContain("alerts", "the document leaves the back-reference out");
    }

    [Test]
    public void No_output_type_is_declared_per_model()
    {
        var files = Render("Alert!", "findAlert(input: $input) { title }");
        var types = files.Single(f => f.Path == "types.g.ts").Text;

        types
            .Should()
            .Contain("export type Level = \"LOW\" | \"HIGH\";")
            .And.Contain("export interface FindInput {");
        foreach (
            var output in new[] { "Alert", "Campaign", "Shapes", "Box", "Found", "Named", "Person" }
        )
            types
                .Should()
                .NotMatchRegex(
                    $@"export (interface|type) {output}\b",
                    "an answer is typed per document, never per model"
                );
    }

    [Test]
    public void Nullability_and_lists_follow_the_sdl()
    {
        var train = Train(
            "Shapes!",
            "findAlert(input: $input) { strict loose maybe note box { size } }"
        );

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
    public void A_nullable_list_answer_keeps_both_nulls()
    {
        Train("[Box]", "findAlert(input: $input) { size }")
            .Should()
            .Contain(
                """
                export type FindAlertResult = Array<{
                  size: number;
                } | null> | null;
                """
            );
    }

    [Test]
    public void A_union_answer_is_one_object_per_member_told_apart_by_typename()
    {
        var train = Train(
            "Found!",
            "findAlert(input: $input) { __typename ... on Alert { title } ... on Campaign { name } }"
        );

        train
            .Should()
            .Contain(
                """
                export type FindAlertResult = {
                  __typename: "Alert";
                  title: string;
                } | {
                  __typename: "Campaign";
                  /** The campaign's name. */
                  name: string;
                };
                """
            );
    }

    [Test]
    public void A_union_member_the_document_leaves_out_still_arrives_as_its_typename()
    {
        Train("Found!", "findAlert(input: $input) { __typename ... on Alert { title } }")
            .Should()
            .Contain(
                """
                } | {
                  __typename: "Campaign";
                };
                """,
                "a value of a member with no fragment still arrives, with only its __typename"
            );
    }

    [Test]
    public void An_interface_answer_is_one_object_per_implementation()
    {
        Train("Named!", "findAlert(input: $input) { __typename name ... on Person { age } }")
            .Should()
            .Contain(
                """
                export type FindAlertResult = {
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
    public void An_object_the_document_selects_only_by_typename_is_its_typename()
    {
        Train("Empty!", "findAlert(input: $input) { __typename }")
            .Should()
            .Contain(
                """
                export type FindAlertResult = {
                  __typename: "Empty";
                };
                """
            );
    }

    [Test]
    public void An_enum_in_an_answer_is_imported_from_the_types_a_client_sends()
    {
        var train = Train("Alert!", "findAlert(input: $input) { level }");

        train
            .Should()
            .Contain("import type { FindInput, Level } from \"../types.g\";")
            .And.Contain("  level: Level | null;");
    }

    [Test]
    public void An_alias_names_its_key()
    {
        Train("Alert!", "findAlert(input: $input) { heading: title }")
            .Should()
            .Contain("  heading: string;")
            .And.NotContain("  title:");
    }

    [Test]
    public void A_document_selecting_a_field_the_types_do_not_hold_is_refused()
    {
        var render = () => Render("Alert!", "findAlert(input: $input) { colour }");

        render
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*findAlert*Alert.colour*Export the manifest again*");
    }

    [Test]
    public void The_progress_module_types_runProgress_from_its_documents()
    {
        var files = new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(ClientManifests.Sample() with { Machines = [] })
        );
        var progress = files.Single(f => f.Path == "progress.g.ts").Text;

        progress
            .Should()
            .Contain("import type { RunProgressState } from \"./types.g\";")
            .And.Contain("export type RunProgress = {")
            .And.Contain("  state: RunProgressState | null;")
            .And.Contain("export type RunProgressEvent = {")
            .And.Contain("  sequence: number;");
        files
            .Single(f => f.Path == "index.g.ts")
            .Text.Should()
            .Contain("type RunProgress, type RunProgressEvent, type RunProgressView");
        files
            .Single(f => f.Path == "types.g.ts")
            .Text.Should()
            .NotContain("JunctionStep", "an answer's object types are not declared per model");
    }

    private static string Train(string answer, string selection) =>
        Render(answer, selection).Single(f => f.Path == "trains/findAlert.g.ts").Text;

    private static IReadOnlyList<ClientFile> Render(string answer, string selection) =>
        new ClientGenerator(new FakeNodeRunner()).Render(
            new ClientGenerateOptions(
                new ClientManifest
                {
                    Trains =
                    [
                        new ClientManifestTrain
                        {
                            Field = "findAlert",
                            Root = "query",
                            Answer = answer,
                            Documents =
                            [
                                new ClientManifestDocument
                                {
                                    Id =
                                        "trax_train_query__findAlert_"
                                        + new string('a', 64)
                                        + new string('b', 16),
                                    Operation = "findAlert",
                                    Document =
                                        "query FindAlert($input: FindInput!) { discover { "
                                        + selection
                                        + " } }",
                                },
                            ],
                        },
                    ],
                    Types = Types,
                }
            )
        );
}
