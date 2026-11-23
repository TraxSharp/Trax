using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// End-to-end exercise of the full generate path: schema parse + dotnet new
/// (trax-hub template) + project reference wiring + Program.cs patching. The template comes from a private
/// hive (<see cref="TemplateHive"/>), so this runs in CI rather than skipping when the machine lacks it.
/// </summary>
[TestFixture]
public class GenerateEndToEndTests
{
    private string _outputDir = "";
    private TemplateHive? _hive;

    [OneTimeSetUp]
    public async Task OneTimeSetUp() =>
        _hive = await TemplateHive.InstallAsync().ConfigureAwait(false);

    [OneTimeTearDown]
    public void OneTimeTearDown() => _hive?.Dispose();

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
    public async Task Generate_FromGraphqlSchema_ProducesHubAndTrainsProjects()
    {
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(FixturePath("simple.graphql"))
            .ConfigureAwait(false);
        var generator = _hive.NotNull().Generator();

        await generator
            .GenerateAsync(schema, _outputDir, "MyApi", force: false)
            .ConfigureAwait(false);

        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Trains")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "MyApi.Hub", "MyApi.Hub.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "MyApi.Trains", "MyApi.Trains.csproj"))
            .Should()
            .BeTrue();

        // Hub csproj has the project reference to trains
        var hubCsproj = await File.ReadAllTextAsync(
                Path.Combine(_outputDir, "MyApi.Hub", "MyApi.Hub.csproj")
            )
            .ConfigureAwait(false);
        hubCsproj.Should().Contain("MyApi.Trains.csproj");

        // Hub Program.cs scans the trains assembly
        var programCs = await File.ReadAllTextAsync(
                Path.Combine(_outputDir, "MyApi.Hub", "Program.cs")
            )
            .ConfigureAwait(false);
        programCs.Should().Contain("MyApi.Trains");
    }

    [Test]
    public async Task Generate_TwiceWithForce_OverwritesExistingDirectory()
    {
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(FixturePath("simple.graphql"))
            .ConfigureAwait(false);
        var generator = _hive.NotNull().Generator();

        await generator
            .GenerateAsync(schema, _outputDir, "MyApi", force: false)
            .ConfigureAwait(false);
        // Second call with force should not throw
        Func<Task> act = () => generator.GenerateAsync(schema, _outputDir, "MyApi", force: true);

        await act.Should().NotThrowAsync().ConfigureAwait(false);
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateCommand_HandleHappyPath_RunsToCompletion()
    {
        var schema = new FileInfo(FixturePath("simple.graphql"));
        var output = new DirectoryInfo(_outputDir);

        Func<Task> act = () =>
            GenerateCommand.HandleAsync(
                schema,
                output,
                "MyApi",
                type: null,
                force: false,
                TextWriter.Null,
                TextWriter.Null,
                _hive.NotNull().Generator()
            );

        await act.Should().NotThrowAsync().ConfigureAwait(false);
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
    }
}
