using System.CommandLine;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Tests.Fakes;
using Trax.Cli.Tests.IntegrationTests;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class GenerateCommandTests
{
    private string _tempDir = "";
    private int _originalExitCode;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trax-cli-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _originalExitCode = Environment.ExitCode;
        Environment.ExitCode = 0;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        Environment.ExitCode = _originalExitCode;
    }

    [Test]
    public void Create_ReturnsGenerateCommandWithExpectedOptions()
    {
        var command = GenerateCommand.Create();

        command.Name.Should().Be("generate");
        command.Description.Should().Contain("Generate");
        command.Options.Should().HaveCountGreaterThan(0);
        command
            .Options.Select(o => o.Name)
            .Should()
            .Contain(new[] { "--schema", "--output", "--name", "--type", "--force" });
    }

    [Test]
    public async Task Handle_SchemaMissing_SetsExitCode1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "missing.graphql"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "Proj",
                        type: null,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var stderr = run.Stderr;

        Environment.ExitCode.Should().Be(1);
        stderr.Should().Contain("Schema file not found");
    }

    [Test]
    public async Task Handle_OutputExistsWithoutForce_SetsExitCode1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.graphql"));
        await File.WriteAllTextAsync(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "existing"));
        Directory.CreateDirectory(output.FullName);

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "Proj",
                        type: null,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var stderr = run.Stderr;

        Environment.ExitCode.Should().Be(1);
        stderr.Should().Contain("already exists");
        stderr.Should().Contain("--force");
    }

    [Test]
    public async Task Handle_with_an_unknown_type_reports_it_and_exits_1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.graphql"));
        await File.WriteAllTextAsync(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "Proj",
                        "swagger",
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exitCode = run.ExitCode;
        var stderr = run.Stderr;

        exitCode.Should().Be(1);
        stderr.Should().Contain("Unknown schema type 'swagger'");
        output.Exists.Should().BeFalse();
    }

    [Test]
    public async Task Handle_with_an_extension_it_cannot_detect_reports_it_and_exits_1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.txt"));
        await File.WriteAllTextAsync(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "Proj",
                        type: null,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exitCode = run.ExitCode;
        var stderr = run.Stderr;

        exitCode.Should().Be(1);
        stderr.Should().Contain("Use --type");
    }

    [TestCase("openapi-3.1.yaml", "OpenAPI 3.1.0")]
    [TestCase("compose.yml", "is not an OpenAPI document")]
    [TestCase("broken.json", "Cannot read")]
    [TestCase("broken.graphql", "is not valid GraphQL SDL")]
    public async Task Handle_with_a_schema_it_cannot_read_reports_it_and_exits_1(
        string file,
        string expected
    )
    {
        var schema = new FileInfo(
            Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Fixtures",
                "UnreadableSchemas",
                file
            )
        );
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "Proj",
                        type: null,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exitCode = run.ExitCode;
        var stderr = run.Stderr;

        exitCode.Should().Be(1);
        stderr.Should().Contain(expected).And.Contain(file);
        stderr.Should().NotContain(" at ", "the refusal is a message, not a stack trace");
        output.Exists.Should().BeFalse();
    }

    [Test]
    public async Task Create_ParseAndInvoke_MissingSchema_ReportsError()
    {
        var command = GenerateCommand.Create();
        var schemaPath = Path.Combine(_tempDir, "no-such-file.graphql");
        var outputPath = Path.Combine(_tempDir, "out");

        var stderr = (
            await CommandOutput
                .InvokeAsync(
                    command.Parse(
                        new[] { "--schema", schemaPath, "--output", outputPath, "--name", "MyProj" }
                    )
                )
                .ConfigureAwait(false)
        ).Stderr;

        stderr.Should().Contain("Schema file not found");
        Environment.ExitCode.Should().Be(1);
    }

    [Test]
    public async Task Handle_SchemaWithAnInvalidName_ReportsTheNameAndReturns1()
    {
        var schema = new FileInfo(
            Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Fixtures",
                "InvalidSchemas",
                "invalid-tag.json"
            )
        );
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "Proj",
                        type: null,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exitCode = run.ExitCode;
        var stderr = run.Stderr;

        exitCode.Should().Be(1);
        stderr.Should().Contain("group of operation 'ListPlayers'");
        stderr.Should().NotContain(" at Trax.Cli", "the refusal is a message, not a stack trace");
        output.Exists.Should().BeFalse();
    }

    [Test]
    public async Task Invoke_WhenHandleFails_ReturnsExitCode1()
    {
        var command = GenerateCommand.Create();
        var schemaPath = Path.Combine(_tempDir, "no-such-file.graphql");

        var exitCode = (
            await CommandOutput
                .InvokeAsync(
                    command.Parse(
                        new[] { "--schema", schemaPath, "--output", _tempDir + "/o", "--name", "P" }
                    )
                )
                .ConfigureAwait(false)
        ).ExitCode;

        exitCode.Should().Be(1, "Program returns this value as the process exit code");
    }

    [Test]
    public async Task Handle_HappyPathGraphQL_ParsesAndGenerates()
    {
        using var hive = await TemplateHive.InstallAsync().ConfigureAwait(false);

        var schema = new FileInfo(FixturePath("simple.graphql"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "happy-graphql"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "HappyGraphQL",
                        type: null,
                        force: false,
                        o,
                        e,
                        hive.Generator()
                    )
            )
            .ConfigureAwait(false);
        var stdout = run.Stdout;

        Environment.ExitCode.Should().Be(0);
        stdout.Should().Contain("from graphql schema");
        stdout.Should().Contain("Generated Trax project at:");
        stdout.Should().Contain("Next steps");
        Directory.Exists(Path.Combine(output.FullName, "HappyGraphQL.Hub")).Should().BeTrue();
        Directory.Exists(Path.Combine(output.FullName, "HappyGraphQL.Trains")).Should().BeTrue();
    }

    [Test]
    public async Task Handle_HappyPathOpenApi_ParsesAndGenerates()
    {
        using var hive = await TemplateHive.InstallAsync().ConfigureAwait(false);

        var schema = new FileInfo(FixturePath("petstore.json"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "happy-openapi"));

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        schema,
                        output,
                        "HappyOpenApi",
                        type: null,
                        force: false,
                        o,
                        e,
                        hive.Generator()
                    )
            )
            .ConfigureAwait(false);
        var stdout = run.Stdout;

        Environment.ExitCode.Should().Be(0);
        stdout.Should().Contain("from openapi schema");
        stdout.Should().Contain("Generated Trax project at:");
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);
}
