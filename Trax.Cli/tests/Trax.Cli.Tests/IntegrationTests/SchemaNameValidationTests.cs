using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;
using Trax.Core.Extensions;

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

    private string _root = "";
    private string _outputDir = "";

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
    public async Task An_OpenApi_property_name_that_is_not_an_identifier_is_refused()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("invalid-property-name.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*has to match*property of 'CreatePlayerInput'*Emitted*", Adr);
        Directory.Exists(_root).Should().BeFalse("nothing is written for a refused schema; " + Adr);
    }

    [Test]
    public async Task An_OpenApi_tag_that_is_not_an_identifier_is_refused_before_anything_is_written()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("invalid-tag.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*group*outside*", Adr);
        Directory.Exists(_root).Should().BeFalse(Adr);
    }

    [Test]
    public async Task Generate_refuses_the_schema_before_touching_an_existing_output_directory()
    {
        Directory.CreateDirectory(_outputDir);
        var marker = Path.Combine(_outputDir, "keep.txt");
        await File.WriteAllTextAsync(marker, "");
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("invalid-tag.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateAsync(schema, _outputDir, "Api", force: true)
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
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
    public async Task OpenApi_properties_that_become_one_name_after_PascalCase_are_refused()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("duplicate-property-names.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*property of 'Person' 'FirstName'*more than once*", Adr);
        Directory.Exists(_root).Should().BeFalse("nothing is written for a refused schema; " + Adr);
    }

    [Test]
    public async Task OpenApi_enum_values_that_become_one_name_after_PascalCase_are_refused()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("duplicate-property-names.json"))
            .ConfigureAwait(false);

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*enum value of 'Status' 'InProgress'*more than once*", Adr);
    }

    [Test]
    public async Task GraphQL_fields_that_become_one_name_after_PascalCase_are_refused()
    {
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(InvalidFixturePath("duplicate-field-names.graphql"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*property of 'Player' 'FirstName'*more than once*", Adr);
        Directory.Exists(_root).Should().BeFalse(Adr);
    }

    [Test]
    public async Task OpenApi_component_schemas_that_become_one_type_are_refused_naming_both()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("colliding-component-names.json"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*'Dto'*'Billing.Dto'*'Shipping.Dto'*", Adr);
        Directory.Exists(_root).Should().BeFalse("nothing is written for a refused schema; " + Adr);
    }

    [Test]
    public async Task An_OpenApi_type_and_enum_of_one_name_are_refused_naming_both()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("type-and-enum-names.json"))
            .ConfigureAwait(false);

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*type or enum 'Status'*'status' (as 'Status')*'Status'*", Adr);
    }

    [Test]
    public async Task GraphQL_operations_that_become_one_name_are_refused_naming_both()
    {
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(InvalidFixturePath("colliding-operation-names.graphql"))
            .ConfigureAwait(false);

        var act = async () =>
            await new TraxProjectGenerator()
                .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*operation 'UserCount'*'Query.user_count'*'Query.userCount'*", Adr);
        Directory.Exists(_root).Should().BeFalse(Adr);
    }

    [Test]
    public async Task GraphQL_types_that_differ_only_in_case_are_refused()
    {
        // Models/PlayerStats.cs and Models/Playerstats.cs are one file on macOS and Windows.
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(InvalidFixturePath("case-only-type-names.graphql"))
            .ConfigureAwait(false);

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'PlayerStats'*'Playerstats'*", Adr);
    }

    [Test]
    public void Operations_that_differ_only_in_case_are_refused()
    {
        var schema = EmptySchema();
        schema.Operations.Add(Operation("GetUser", "Users"));
        schema.Operations.Add(Operation("Getuser", "Users"));

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'GetUser'*'Getuser'*", Adr);
    }

    [Test]
    public void Groups_that_differ_only_in_case_are_refused()
    {
        var schema = EmptySchema();
        schema.Operations.Add(Operation("GetStats", "PlayerStats"));
        schema.Operations.Add(Operation("ListStats", "Playerstats"));

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*group*'PlayerStats'*'Playerstats'*", Adr);
    }

    [Test]
    public async Task An_OpenApi_inline_object_named_like_a_component_gets_its_own_type()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(FixturePath("inline-name-reuse.json"))
            .ConfigureAwait(false);

        var address = schema.Types.Single(t =>
            string.Equals(t.Name, "Address", StringComparison.Ordinal)
        );
        address
            .Fields.Select(f => f.Name)
            .Should()
            .BeEquivalentTo(["Street", "City"], "the component keeps its own fields");
        var customerAddress = schema
            .Types.Single(t => string.Equals(t.Name, "Customer", StringComparison.Ordinal))
            .Fields.Single(f => string.Equals(f.Name, "Address", StringComparison.Ordinal));
        customerAddress
            .TypeName.Should()
            .NotBe("Address", "the inline object is not the component");
        schema
            .Types.Single(t =>
                string.Equals(t.Name, customerAddress.TypeName, StringComparison.Ordinal)
            )
            .Fields.Select(f => f.Name)
            .Should()
            .BeEquivalentTo(["Line1"]);
    }

    [Test]
    public async Task OpenApi_inline_enums_of_one_name_with_different_values_are_not_merged()
    {
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(FixturePath("inline-name-reuse.json"))
            .ConfigureAwait(false);

        var customerStatus = FieldType(schema, "Customer", "Status");
        var ticketStatus = FieldType(schema, "Ticket", "Status");

        customerStatus
            .Should()
            .NotBe(ticketStatus, "two enums with different values are two enums; " + Adr);
        schema
            .Enums.Single(e => string.Equals(e.Name, customerStatus, StringComparison.Ordinal))
            .Values.Should()
            .Equal("Active", "Suspended");
        schema
            .Enums.Single(e => string.Equals(e.Name, ticketStatus, StringComparison.Ordinal))
            .Values.Should()
            .Equal("Open", "Closed");
    }

    private static string FieldType(ApiSchema schema, string type, string field) =>
        schema
            .Types.Single(t => string.Equals(t.Name, type, StringComparison.Ordinal))
            .Fields.Single(f => string.Equals(f.Name, field, StringComparison.Ordinal))
            .TypeName;

    [Test]
    public async Task OpenApi_parameters_that_become_one_input_property_are_refused()
    {
        // update_value (path) and updateValue (query) both become UpdateValue. The parser used to
        // keep the first and drop the second without a word.
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("converging-input-names.json"))
            .ConfigureAwait(false);

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*property of 'UpdateItemInput' 'UpdateValue'*more than once*", Adr);
    }

    [Test]
    public async Task An_OpenApi_path_parameter_repeated_in_the_body_is_one_input_property()
    {
        // PUT /players/{id} with a body that also carries id: the same name for the same value.
        var schema = await new OpenApiSchemaParser()
            .ParseAsync(InvalidFixturePath("converging-input-names.json"))
            .ConfigureAwait(false);

        var input = schema
            .Operations.Single(o =>
                string.Equals(o.Name, "ReplacePlayer", StringComparison.Ordinal)
            )
            .InputType;

        input.Fields.Select(f => f.Name).Should().Equal(["Id", "Name"], Adr);
    }

    private static ApiOperation Operation(string name, string group) =>
        new()
        {
            Name = name,
            Kind = Trax.Cli.Models.OperationKind.Query,
            Group = group,
            InputType = new ApiType { Name = $"{name}Input" },
            OutputType = new ApiType { Name = "Unit", IsBuiltIn = true },
        };

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
    public async Task A_schema_whose_text_contains_code_generates_a_library_that_compiles_as_data()
    {
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(FixturePath("description-text.graphql"))
            .ConfigureAwait(false);
        await new TraxProjectGenerator()
            .GenerateTrainsLibraryAsync(schema, _outputDir, "Api")
            .ConfigureAwait(false);

        var trees = (await GeneratedSources.ReadAsync(_outputDir).ConfigureAwait(false))
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

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES").NotNull())
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

        var declared = new List<string>();
        foreach (var tree in trees)
            declared.AddRange(
                (await tree.GetRootAsync().ConfigureAwait(false))
                    .DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .Select(t => t.Identifier.Text)
            );
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
