using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// Schema shapes that once generated a trains library referring to a type it never wrote. Each parses a small
/// schema, writes the library with <c>GenerateTrainsLibrary</c> and compiles every file with Roslyn, with the
/// implicit usings its csproj turns on, against the Trax assemblies the tests deploy.
/// </summary>
public class ScaffoldFromSchemaCompilesTests
{
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-cli-compiles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void A_GraphQL_custom_scalar_field_generates_a_library_that_compiles()
    {
        var schema = ParseGraphQL(
            """
            scalar UUID
            scalar JSON
            type Player { id: UUID! data: JSON tags: [JSON!] }
            type Query { player(id: UUID!): Player playerId(name: String!): UUID! }
            """
        );

        var player = schema.Types.Single(t => t.Name == "Player");
        player.Fields.Select(f => f.TypeName).Should().Equal("string", "string", "List<string>");
        player.Fields[0].Description.Should().Contain("TODO").And.Contain("UUID");
        AssertCompiles(schema);
    }

    [Test]
    public void A_GraphQL_interface_or_union_field_generates_a_library_that_compiles()
    {
        var schema = ParseGraphQL(
            """
            interface Node { id: ID! }
            type Player implements Node { id: ID! name: String! }
            type Team implements Node { id: ID! }
            union SearchResult = Player | Team
            type Holder { node: Node results: [SearchResult!]! }
            type Query { holder: Holder search(term: String!): [SearchResult!]! }
            """
        );

        var holder = schema.Types.Single(t => t.Name == "Holder");
        holder.Fields.Select(f => f.TypeName).Should().Equal("object", "List<object>");
        holder.Fields[1].Description.Should().Contain("TODO").And.Contain("SearchResult");
        AssertCompiles(schema);
    }

    [Test]
    public void A_GraphQL_field_named_like_its_type_is_refused_naming_both()
    {
        var schema = ParseGraphQL(
            """
            type Status { status: String! }
            type Query { status: Status }
            """
        );

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*property 'Status' of 'Status'*cannot have its type's name*");
    }

    [Test]
    public void An_OpenApi_map_of_an_empty_component_generates_a_library_that_compiles()
    {
        // Player is declared before the empty Score it maps to, and before the empty Tag a nested list holds.
        var schema = ParseOpenApi(
            """
            openapi: 3.0.1
            info: { title: t, version: "1" }
            paths:
              /players:
                get:
                  operationId: listPlayers
                  responses:
                    "200":
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              byName:
                                type: object
                                additionalProperties: { $ref: '#/components/schemas/Score' }
                              players:
                                type: array
                                items: { $ref: '#/components/schemas/Player' }
            components:
              schemas:
                Player:
                  type: object
                  properties:
                    name: { type: string }
                    scores:
                      type: object
                      additionalProperties: { $ref: '#/components/schemas/Score' }
                    tagGrid:
                      type: array
                      items:
                        type: array
                        items: { $ref: '#/components/schemas/Tag' }
                    profile:
                      type: object
                      properties:
                        best: { $ref: '#/components/schemas/Score' }
                        nickname: { type: string }
                Score: { type: object }
                Tag: { type: object }
            """
        );

        schema
            .Types.Single(t => t.Name == "Player")
            .Fields.Select(f => f.TypeName)
            .Should()
            .Contain(["Dictionary<string, object>", "List<List<object>>"]);
        AssertCompiles(schema);
    }

    private ApiSchema ParseGraphQL(string sdl)
    {
        var path = Path.Combine(_dir, "schema.graphql");
        File.WriteAllText(path, sdl);
        return new GraphQLSchemaParser().Parse(path);
    }

    private ApiSchema ParseOpenApi(string yaml)
    {
        var path = Path.Combine(_dir, "schema.yaml");
        File.WriteAllText(path, yaml);
        return new OpenApiSchemaParser().Parse(path);
    }

    private void AssertCompiles(ApiSchema schema)
    {
        var output = Path.Combine(_dir, "Api.Trains");
        new TraxProjectGenerator().GenerateTrainsLibrary(schema, output, "Api");

        var sources = Directory
            .EnumerateFiles(output, "*.cs", SearchOption.AllDirectories)
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, path: "GlobalUsings.cs"));

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        var errors = CSharpCompilation
            .Create(
                "Scaffold",
                sources,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            )
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        errors.Should().BeEmpty("a generated library must build as written");
    }
}
