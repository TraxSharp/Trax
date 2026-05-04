using FluentAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// End-to-end exercise of the full generate path: schema parse + dotnet new
/// (trax-hub template) + project reference wiring + Program.cs patching.
/// Requires the trax-hub template to be installed (it is via Trax.Samples.Templates).
/// </summary>
[TestFixture]
public class GenerateEndToEndTests
{
    private string _outputDir = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // The trax-hub template ships in Trax.Samples.Templates. CI doesn't always
        // install it, so skip this fixture rather than fail when it's missing.
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", "new list trax-hub")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (!stdout.Contains("trax-hub"))
            Assert.Ignore("trax-hub template is not installed (Trax.Samples.Templates).");
    }

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
        var generator = new TraxProjectGenerator();

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
        var generator = new TraxProjectGenerator();

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

        Action act = () => GenerateCommand.Handle(schema, output, "MyApi", null, force: false);

        act.Should().NotThrow();
        Directory.Exists(Path.Combine(_outputDir, "MyApi.Hub")).Should().BeTrue();
    }
}
