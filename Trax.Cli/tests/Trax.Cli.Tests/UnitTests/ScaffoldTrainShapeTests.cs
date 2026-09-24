using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Every train the generator scaffolds declares its chain in <c>Junctions()</c>, and nothing it
/// writes uses <c>RunInternal</c> or <c>Activate(</c>, the API the template used before.
///
/// <para><c>ScaffoldCompilationTests</c> cannot catch a return to the old template: it compiles
/// against the Trax.Core the tests deploy, which a transitive pin holds at a version where
/// <c>RunInternal</c> and <c>Activate</c> still exist, so old-shape code still compiles there. A
/// scaffold only breaks against the Trax a new project floats to. This parses the generated
/// source instead, for every fixture schema, which covers query, mutation and Unit-output
/// trains. It checks syntax rather than text, so a block-bodied <c>Junctions()</c> passes and a
/// comment that mentions <c>Activate(</c> does not fail it.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class ScaffoldTrainShapeTests
{
    private string _outputDir = null!;

    [SetUp]
    public void SetUp() =>
        _outputDir = Path.Combine(Path.GetTempPath(), $"trax-cli-shape-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    private static IEnumerable<string> FixtureSchemas() =>
        Directory
            .EnumerateFiles(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas")
            )
            .Select(Path.GetFileName)
            .Cast<string>()
            .Order();

    [TestCaseSource(nameof(FixtureSchemas))]
    public void Every_scaffolded_train_declares_its_chain_in_Junctions(string fixture)
    {
        var schema = Parse(fixture);
        new TraxProjectGenerator().GenerateTrainsLibrary(schema, _outputDir, "Shape");

        var sources = Directory
            .EnumerateFiles(_outputDir, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(_outputDir, f), File.ReadAllText);

        // Where the generator writes each operation's train implementation. Named per operation
        // rather than matched on "*Train.cs", which would also match the I*Train.cs interfaces.
        var trains = schema
            .Operations.Select(o =>
                Path.Combine("Trains", o.Group ?? "General", o.Name, $"{o.Name}Train.cs")
            )
            .Select(path =>
                (
                    path,
                    source: sources.GetValueOrDefault(path)
                        ?? throw new AssertionException($"no train was written at {path}")
                )
            )
            .ToList();

        trains.Should().NotBeEmpty("every fixture schema has at least one operation");

        foreach (var (path, source) in trains)
        {
            var trainClass = CSharpSyntaxTree
                .ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .SingleOrDefault(c =>
                    c.Identifier.ValueText == Path.GetFileNameWithoutExtension(path)
                );

            trainClass.Should().NotBeNull($"{path} must declare the train class it is named for");

            trainClass!
                .Members.OfType<MethodDeclarationSyntax>()
                .Should()
                .Contain(
                    m =>
                        m.Identifier.ValueText == "Junctions"
                        && m.Modifiers.Any(SyntaxKind.OverrideKeyword),
                    $"{path} must declare its chain by overriding Junctions(), which current Trax requires"
                );
        }

        foreach (var (path, source) in sources)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();

            root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(m => MemberName(m) == "RunInternal")
                .Should()
                .BeEmpty($"{path} would not compile against the Trax a new scaffold references");

            root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(i => InvokedName(i.Expression) == "Activate")
                .Should()
                .BeEmpty($"{path} would not compile against the Trax a new scaffold references");
        }
    }

    private static string? MemberName(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax m => m.Identifier.ValueText,
            PropertyDeclarationSyntax p => p.Identifier.ValueText,
            _ => null,
        };

    /// <summary>
    /// The simple name an invocation calls, whether written <c>Activate(...)</c>,
    /// <c>this.Activate(...)</c> or <c>Activate&lt;T&gt;(...)</c>.
    /// </summary>
    private static string? InvokedName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
            SimpleNameSyntax name => name.Identifier.ValueText,
            _ => null,
        };

    [Test]
    public void The_fixtures_include_a_Unit_output_train()
    {
        FixtureSchemas()
            .SelectMany(f => Parse(f).Operations)
            .Should()
            .Contain(
                o => o.OutputType.Name == "Unit",
                "the Unit branch of the train and junction templates is otherwise unexercised here"
            );
    }

    private static ApiSchema Parse(string fixture)
    {
        var path = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            "Schemas",
            fixture
        );
        return fixture.EndsWith(".graphql", StringComparison.Ordinal)
            ? new GraphQLSchemaParser().Parse(path)
            : new OpenApiSchemaParser().Parse(path);
    }
}
