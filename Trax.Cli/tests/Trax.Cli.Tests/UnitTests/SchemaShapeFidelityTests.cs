using AwesomeAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// What the parsers read must keep the shape the schema declares: every operation it declares, lists as lists,
/// the value an endpoint returns, and no members the schema never sends. Each of these once compiled and
/// generated a contract that silently differed from the schema.
/// </summary>
public class SchemaShapeFidelityTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-shape-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private ApiSchema ParseGraphQL(string sdl)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.graphql");
        File.WriteAllText(path, sdl);
        return new GraphQLSchemaParser().Parse(path);
    }

    private ApiSchema ParseOpenApi(string yaml)
    {
        var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        return new OpenApiSchemaParser().Parse(path);
    }

    [Test]
    public void GraphQL_fields_added_by_extend_type_Query_and_Mutation_become_operations()
    {
        var schema = ParseGraphQL(
            """
            type Player { id: ID! name: String! }
            type Query { players: [Player!]! }
            type Mutation { renamePlayer(id: ID!, name: String!): Player! }
            extend type Query { playerCount: Int! }
            extend type Mutation { deletePlayer(id: ID!): Boolean! }
            extend type Player { score: Int }
            """
        );

        schema
            .Operations.Select(o => o.Name)
            .Should()
            .BeEquivalentTo(["Players", "RenamePlayer", "PlayerCount", "DeletePlayer"]);
        schema
            .Types.Single(t => t.Name == "Player")
            .Fields.Select(f => f.Name)
            .Should()
            .Equal("Id", "Name", "Score");
    }

    [Test]
    public void GraphQL_extension_of_a_type_the_schema_never_defines_is_refused()
    {
        var act = () => ParseGraphQL("extend type Query { playerCount: Int! }");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*extends 'Query'*never defines*");
    }

    [Test]
    public void GraphQL_single_list_of_input_objects_argument_stays_a_list()
    {
        var schema = ParseGraphQL(
            """
            type Player { id: ID! name: String! }
            input PlayerInput { name: String! }
            type Query { players: [Player!]! }
            type Mutation {
              createPlayers(inputs: [PlayerInput!]!): [Player!]!
              createPlayer(input: PlayerInput!): Player!
            }
            """
        );

        var batch = schema.Operations.Single(o => o.Name == "CreatePlayers").InputType;
        batch
            .Fields.Select(f => (f.Name, f.TypeName))
            .Should()
            .Equal(("Inputs", "List<PlayerInput>"));
        var single = schema.Operations.Single(o => o.Name == "CreatePlayer").InputType;
        single.Fields.Select(f => (f.Name, f.TypeName)).Should().Equal(("Name", "string"));
    }

    [Test]
    public void OpenApi_nullable_enum_does_not_turn_null_into_an_enum_member()
    {
        var schema = ParseOpenApi(
            """
            openapi: 3.0.1
            info: { title: t, version: "1" }
            paths: {}
            components:
              schemas:
                Status:
                  type: string
                  nullable: true
                  enum: [active, banned, null]
                Player:
                  type: object
                  properties:
                    mood:
                      type: string
                      nullable: true
                      enum: [happy, null]
            """
        );

        schema.Enums.Single(e => e.Name == "Status").Values.Should().Equal("Active", "Banned");
        schema.Enums.Single(e => e.Name == "Mood").Values.Should().Equal("Happy");
    }

    [Test]
    public void OpenApi_string_enum_with_a_value_that_is_not_a_string_is_refused()
    {
        var act = () =>
            ParseOpenApi(
                """
                openapi: 3.0.1
                info: { title: t, version: "1" }
                paths: {}
                components:
                  schemas:
                    Flag:
                      type: string
                      enum: [on, { a: 1 }]
                """
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Flag'*not a string*");
    }

    [Test]
    public void OpenApi_ref_to_a_primitive_component_keeps_the_primitive_type()
    {
        var schema = ParseOpenApi(
            """
            openapi: 3.0.1
            info: { title: t, version: "1" }
            paths:
              /players/{id}/name:
                get:
                  operationId: getPlayerName
                  parameters:
                    - { name: id, in: path, required: true, schema: { $ref: '#/components/schemas/PlayerId' } }
                  responses:
                    "200":
                      description: ok
                      content:
                        application/json:
                          schema: { $ref: '#/components/schemas/PlayerName' }
            components:
              schemas:
                Player:
                  type: object
                  properties:
                    id: { $ref: '#/components/schemas/PlayerId' }
                    score: { $ref: '#/components/schemas/Score' }
                PlayerId: { type: string, format: uuid }
                PlayerName: { type: string }
                Score: { type: integer, format: int64 }
            """
        );

        var operation = schema.Operations.Single();
        operation.InputType.Fields.Single().TypeName.Should().Be("Guid");
        operation.OutputType.IsBuiltIn.Should().BeFalse("the value the endpoint returns is kept");
        operation
            .OutputType.Fields.Select(f => (f.Name, f.TypeName))
            .Should()
            .Equal(("Value", "string"));
        schema
            .Types.Single(t => t.Name == "Player")
            .Fields.Select(f => f.TypeName)
            .Should()
            .Equal("Guid", "long");
        schema.Types.Select(t => t.Name).Should().NotContain(["PlayerId", "PlayerName", "Score"]);
    }

    private const string PlayerComponent = """
        components:
          schemas:
            Player:
              type: object
              properties:
                name: { type: string }
            Players:
              type: array
              items: { $ref: '#/components/schemas/Player' }
            PlayerId: { type: string, format: uuid }
        """;

    private ApiSchema ParseBody(string body, string extra = "") =>
        ParseOpenApi(
            $$"""
            openapi: 3.0.1
            info: { title: t, version: "1" }
            paths:
              /players/batch:
                post:
                  operationId: createPlayers
                  {{extra}}
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema: {{body}}
                  responses:
                    "204": { description: ok }
            {{PlayerComponent}}
            """
        );

    [Test]
    public void OpenApi_inline_array_request_body_reaches_the_train_input()
    {
        var input = ParseBody("{ type: array, items: { $ref: '#/components/schemas/Player' } }")
            .Operations.Single()
            .InputType;

        input.Fields.Select(f => (f.Name, f.TypeName)).Should().Equal(("Body", "List<Player>"));
        input.Fields.Single().IsRequired.Should().BeTrue();
    }

    [TestCase("{ type: string }", "string")]
    [TestCase(
        "{ type: object, additionalProperties: { type: integer } }",
        "Dictionary<string, int>"
    )]
    [TestCase("{ $ref: '#/components/schemas/PlayerId' }", "Guid")]
    public void OpenApi_request_body_without_properties_is_one_field(string body, string type)
    {
        ParseBody(body)
            .Operations.Single()
            .InputType.Fields.Select(f => (f.Name, f.TypeName))
            .Should()
            .Equal(("Body", type));
    }

    [Test]
    public void OpenApi_request_body_field_takes_the_codegen_body_name()
    {
        ParseBody("{ type: array, items: { type: string } }", "x-codegen-request-body-name: names")
            .Operations.Single()
            .InputType.Fields.Select(f => (f.Name, f.TypeName))
            .Should()
            .Equal(("Names", "List<string>"));
    }

    [Test]
    public void OpenApi_ref_to_an_array_component_request_body_keeps_its_items()
    {
        ParseBody("{ $ref: '#/components/schemas/Players' }")
            .Operations.Single()
            .InputType.Fields.Select(f => (f.Name, f.TypeName))
            .Should()
            .Equal(("Items", "List<Player>"));
    }

    [Test]
    public void OpenApi_inline_allOf_request_body_spreads_every_member()
    {
        ParseBody(
            "{ allOf: [ { $ref: '#/components/schemas/Player' }, { type: object, properties: { team: { type: string } } } ] }"
        )
            .Operations.Single()
            .InputType.Fields.Select(f => f.Name)
            .Should()
            .Equal("Name", "Team");
    }

    [Test]
    public void OpenApi_empty_object_request_body_adds_no_field()
    {
        ParseBody("{ type: object }").Operations.Single().InputType.Fields.Should().BeEmpty();
    }

    [TestCase("settings", "Settings")]
    [TestCase("addresses", "Addresses")]
    [TestCase("updates", "Updates")]
    [TestCase("listings", "Listings")]
    [TestCase("patches", "Patches")]
    [TestCase("postcodes", "Postcodes")]
    [TestCase("addPlayer", "Players")]
    [TestCase("updatePlayer", "Players")]
    [TestCase("listTeams", "Teams")]
    public void DeriveGroupName_strips_a_verb_only_at_a_word_boundary(string field, string group)
    {
        NamingConventions.DeriveGroupName(field).Should().Be(group);
    }
}
