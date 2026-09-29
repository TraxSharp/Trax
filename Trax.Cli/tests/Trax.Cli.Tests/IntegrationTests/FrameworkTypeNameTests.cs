using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// A schema may name its models after types the generated code also uses (<c>Task</c>, <c>Exception</c>,
/// <c>File</c>, <c>ILogger</c>), and with the implicit usings of a new project each of those was ambiguous
/// (CS0104) wherever a train referred to it. The generated library is compiled here as a whole, with the
/// implicit usings its csproj turns on, against the Trax assemblies the tests deploy.
///
/// <para>Enforces cli/0002 (<c>docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md</c>).</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md")]
public class FrameworkTypeNameTests
{
    private const string Adr =
        "see cli/0002 (docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md)";

    // What <ImplicitUsings>enable</ImplicitUsings> adds for Microsoft.NET.Sdk.
    private const string ImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private string _outputDir = null!;

    [SetUp]
    public void SetUp() =>
        _outputDir = Path.Combine(Path.GetTempPath(), $"trax-cli-framework-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [Test]
    public void A_library_whose_models_are_named_like_framework_types_compiles()
    {
        var schema = new GraphQLSchemaParser().Parse(
            Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Fixtures",
                "Schemas",
                "framework-type-names.graphql"
            )
        );

        new TraxProjectGenerator().GenerateTrainsLibrary(schema, _outputDir, "Api");

        var sources = Directory
            .EnumerateFiles(_outputDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
            .Append(CSharpSyntaxTree.ParseText(ImplicitUsings, path: "GlobalUsings.cs"));

        var errors = Compile(sources)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        errors.Should().BeEmpty("a model may share its name with a framework type; " + Adr);
    }

    [TestCase("Unit")]
    [TestCase("Guid")]
    [TestCase("DateTime")]
    [TestCase("DateOnly")]
    [TestCase("Uri")]
    public void A_model_named_like_a_type_the_parsers_emit_is_refused(string name)
    {
        // The parsers write Guid, DateTime, DateOnly and Uri for formatted strings, and Unit for an
        // operation that returns nothing; a model of that name makes them mean the model instead.
        var schema = new ApiSchema { SourceFile = "x", SchemaType = "graphql" };
        schema.Types.Add(
            new ApiType
            {
                Name = name,
                Fields = [new ApiField { Name = "Id", TypeName = "string" }],
            }
        );

        var act = () => SchemaNames.Validate(schema, "Api");

        act.Should().Throw<InvalidOperationException>().WithMessage($"*'{name}'*reserved*", Adr);
    }

    private static IEnumerable<Diagnostic> Compile(IEnumerable<SyntaxTree> sources)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation
            .Create(
                "Scaffold",
                sources,
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            )
            .GetDiagnostics();
    }
}
