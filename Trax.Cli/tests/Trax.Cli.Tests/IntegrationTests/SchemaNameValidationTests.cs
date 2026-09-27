using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// Names from a schema become identifiers, namespaces and file paths in the generated project, so
/// the generator writes one only when it matches <c>[A-Za-z_][A-Za-z0-9_]*</c> and refuses the
/// schema otherwise, before anything is written. Text that is not a name (descriptions) is
/// rendered as data, which the compile test at the bottom checks end to end.
///
/// <para>Enforces cli/0002 (<c>docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md</c>).</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md")]
public class SchemaNameValidationTests
{
    private const string Adr =
        "see cli/0002 (docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md)";

    private string _root = null!;
    private string _outputDir = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-cli-names-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_root, "a", "b", "out");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    // Kept out of Fixtures/Schemas, which other tests generate every file of.
    private static string InvalidFixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "InvalidSchemas", name);

    [Test]
    public void An_OpenApi_property_name_that_is_not_an_identifier_is_refused()
    {
        var schema = new OpenApiSchemaParser().Parse(
            InvalidFixturePath("invalid-property-name.json")
        );

        var act = () => new TraxProjectGenerator().GenerateTrainsLibrary(schema, _outputDir, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*has to match*property of 'CreatePlayerInput'*Emitted*", Adr);
        Directory.Exists(_root).Should().BeFalse("nothing is written for a refused schema; " + Adr);
    }

    [Test]
    public void An_OpenApi_tag_that_is_not_an_identifier_is_refused_before_anything_is_written()
    {
        var schema = new OpenApiSchemaParser().Parse(InvalidFixturePath("invalid-tag.json"));

        var act = () => new TraxProjectGenerator().GenerateTrainsLibrary(schema, _outputDir, "Api");

        act.Should().Throw<InvalidOperationException>().WithMessage("*group*outside*", Adr);
        Directory.Exists(_root).Should().BeFalse(Adr);
    }

    [Test]
    public void Generate_refuses_the_schema_before_touching_an_existing_output_directory()
    {
        Directory.CreateDirectory(_outputDir);
        var marker = Path.Combine(_outputDir, "keep.txt");
        File.WriteAllText(marker, "");
        var schema = new OpenApiSchemaParser().Parse(InvalidFixturePath("invalid-tag.json"));

        var act = () => new TraxProjectGenerator().Generate(schema, _outputDir, "Api", force: true);

        act.Should().Throw<InvalidOperationException>();
        File.Exists(marker)
            .Should()
            .BeTrue("--force must not delete anything for a refused schema");
    }

    [TestCase("My.Api")]
    [TestCase("MyApi")]
    public void A_dotted_project_name_is_accepted(string projectName)
    {
        var act = () => SchemaNames.Validate(EmptySchema(), projectName);

        act.Should().NotThrow();
    }

    [TestCase("My-Api")]
    [TestCase("My..Api")]
    [TestCase("1Api")]
    [TestCase("Api; class X")]
    [TestCase("")]
    public void A_project_name_that_is_not_a_dotted_identifier_is_refused(string projectName)
    {
        var act = () => SchemaNames.Validate(EmptySchema(), projectName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*project name*");
    }

    [TestCase("List<Player>")]
    [TestCase("Dictionary<string, List<int>>")]
    [TestCase("byte[]")]
    [TestCase("int?")]
    public void A_field_type_expression_is_accepted(string typeName)
    {
        var act = () => SchemaNames.Validate(SchemaWithFieldType(typeName), "Api");

        act.Should().NotThrow();
    }

    [TestCase("List<Player>> x; class Y<Z")]
    [TestCase("Player;")]
    [TestCase("List<>")]
    [TestCase("Application/json")]
    public void A_field_type_that_is_not_a_type_expression_is_refused(string typeName)
    {
        var act = () => SchemaNames.Validate(SchemaWithFieldType(typeName), "Api");

        act.Should().Throw<InvalidOperationException>().WithMessage("*type*");
    }

    [Test]
    public void An_enum_value_that_is_not_an_identifier_is_refused()
    {
        var schema = EmptySchema();
        schema.Enums.Add(new ApiEnum { Name = "Mime", Values = ["Application/json"] });

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*enum value*Application/json*");
    }

    [Test]
    public void Every_invalid_name_is_reported_at_once()
    {
        var schema = SchemaWithFieldType("Bad;");
        schema.Enums.Add(new ApiEnum { Name = "Bad Enum", Values = ["Ok"] });

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Bad;*Bad Enum*");
    }

    /// <summary>
    /// Descriptions, block strings and keyword enum values from a GraphQL schema, generated and
    /// compiled as a whole library: it compiles, and declares exactly the types the schema asks
    /// for.
    /// </summary>
    [Test]
    public void A_schema_whose_text_contains_code_generates_a_library_that_compiles_as_data()
    {
        var schema = new GraphQLSchemaParser().Parse(FixturePath("description-text.graphql"));
        new TraxProjectGenerator().GenerateTrainsLibrary(schema, _outputDir, "Api");

        var trees = Directory
            .EnumerateFiles(_outputDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
            .Append(
                CSharpSyntaxTree.ParseText(
                    """
                    global using System;
                    global using System.Collections.Generic;
                    global using System.Linq;
                    global using System.Threading.Tasks;
                    """
                )
            )
            .ToList();

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var errors = CSharpCompilation
            .Create(
                "Generated",
                trees,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            )
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        errors.Should().BeEmpty("schema text is rendered as data; " + Adr);

        var declared = trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Select(t => t.Identifier.Text)
            .ToList();
        declared
            .Should()
            .BeEquivalentTo(
                "GraphQLNamespaces",
                "ManifestNames",
                "Player",
                "Color",
                "IGetPlayerTrain",
                "GetPlayerTrain",
                "GetPlayerInput",
                "GetPlayerJunction"
            );
        declared.Should().NotContain(n => n.StartsWith("Emitted"), Adr);
    }

    private static ApiSchema EmptySchema() => new() { SourceFile = "test", SchemaType = "openapi" };

    private static ApiSchema SchemaWithFieldType(string typeName)
    {
        var schema = EmptySchema();
        schema.Types.Add(
            new ApiType
            {
                Name = "Player",
                Fields = [new ApiField { Name = "Value", TypeName = typeName }],
            }
        );
        return schema;
    }
}
