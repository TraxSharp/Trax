using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// End-to-end exercise of the full generate path: schema parse + dotnet new
/// (trax-hub template) + project reference wiring + Program.cs patching. The template comes from a private
/// hive (<see cref="TemplateHive"/>), so this runs in CI rather than skipping when the machine lacks it.
/// </summary>
[TestFixture]
public class GenerateEndToEndTests
{
    private string _outputDir = null!;
    private TemplateHive _hive = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _hive = TemplateHive.Install();

    [OneTimeTearDown]
    public void OneTimeTearDown() => _hive.Dispose();

    [SetUp]
    public void SetUp()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"trax-cli-gen-e2e-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    [Test]
    public void Generate_FromGraphqlSchema_ProducesHubAndTrainsProjects()
    {
        var schema = new GraphQLSchemaParser().Parse(FixturePath("simple.graphql"));
        var generator = _hive.Generator();

        generator.Generate(schema, _outputDir, "MyApi", force: false);

        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Trains")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "MyApi.Hub", "MyApi.Hub.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "MyApi.Trains", "MyApi.Trains.csproj"))
            .Should()
            .BeTrue();

        // Hub csproj has the project reference to trains
        var hubCsproj = File.ReadAllText(Path.Combine(_outputDir, "MyApi.Hub", "MyApi.Hub.csproj"));
        hubCsproj.Should().Contain("MyApi.Trains.csproj");

        // Hub Program.cs scans the trains assembly
        var programCs = File.ReadAllText(Path.Combine(_outputDir, "MyApi.Hub", "Program.cs"));
        programCs.Should().Contain("MyApi.Trains");
    }

    [Test]
    public void Generate_TwiceWithForce_OverwritesExistingDirectory()
    {
        var schema = new GraphQLSchemaParser().Parse(FixturePath("simple.graphql"));
        var generator = _hive.Generator();

        generator.Generate(schema, _outputDir, "MyApi", force: false);
        // Second call with force should not throw
        Action act = () => generator.Generate(schema, _outputDir, "MyApi", force: true);

        act.Should().NotThrow();
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
    }

    [Test]
    public void GenerateCommand_HandleHappyPath_RunsToCompletion()
    {
        var schema = new FileInfo(FixturePath("simple.graphql"));
        var output = new DirectoryInfo(_outputDir);

        Action act = () =>
            GenerateCommand.Handle(schema, output, "MyApi", null, force: false, _hive.Generator());

        act.Should().NotThrow();
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
    }
}
