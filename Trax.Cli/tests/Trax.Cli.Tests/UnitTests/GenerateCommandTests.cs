using System.CommandLine;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Tests.IntegrationTests;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class GenerateCommandTests
{
    private string _tempDir = null!;
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
    public void Handle_SchemaMissing_SetsExitCode1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "missing.graphql"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var stderr = CaptureStderr(() =>
            GenerateCommand.Handle(schema, output, "Proj", null, false)
        );

        Environment.ExitCode.Should().Be(1);
        stderr.Should().Contain("Schema file not found");
    }

    [Test]
    public void Handle_OutputExistsWithoutForce_SetsExitCode1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.graphql"));
        File.WriteAllText(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "existing"));
        Directory.CreateDirectory(output.FullName);

        var stderr = CaptureStderr(() =>
            GenerateCommand.Handle(schema, output, "Proj", null, false)
        );

        Environment.ExitCode.Should().Be(1);
        stderr.Should().Contain("already exists");
        stderr.Should().Contain("--force");
    }

    [Test]
    public void Handle_with_an_unknown_type_reports_it_and_exits_1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.graphql"));
        File.WriteAllText(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var exitCode = 0;
        var stderr = CaptureStderr(() =>
            exitCode = GenerateCommand.Handle(schema, output, "Proj", "swagger", false)
        );

        exitCode.Should().Be(1);
        stderr.Should().Contain("Unknown schema type 'swagger'");
        output.Exists.Should().BeFalse();
    }

    [Test]
    public void Handle_with_an_extension_it_cannot_detect_reports_it_and_exits_1()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.txt"));
        File.WriteAllText(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        var exitCode = 0;
        var stderr = CaptureStderr(() =>
            exitCode = GenerateCommand.Handle(schema, output, "Proj", null, false)
        );

        exitCode.Should().Be(1);
        stderr.Should().Contain("Use --type");
    }

    [TestCase("openapi-3.1.yaml", "OpenAPI 3.1.0")]
    [TestCase("compose.yml", "is not an OpenAPI document")]
    [TestCase("broken.json", "Cannot read")]
    [TestCase("broken.graphql", "is not valid GraphQL SDL")]
    public void Handle_with_a_schema_it_cannot_read_reports_it_and_exits_1(
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

        var exitCode = 0;
        var stderr = CaptureStderr(() =>
            exitCode = GenerateCommand.Handle(schema, output, "Proj", null, false)
        );

        exitCode.Should().Be(1);
        stderr.Should().Contain(expected).And.Contain(file);
        stderr.Should().NotContain(" at ", "the refusal is a message, not a stack trace");
        output.Exists.Should().BeFalse();
    }

    [Test]
    public void Create_ParseAndInvoke_MissingSchema_ReportsError()
    {
        var command = GenerateCommand.Create();
        var schemaPath = Path.Combine(_tempDir, "no-such-file.graphql");
        var outputPath = Path.Combine(_tempDir, "out");

        var stderr = CaptureStderr(() =>
            command
                .Parse(new[] { "--schema", schemaPath, "--output", outputPath, "--name", "MyProj" })
                .Invoke()
        );

        stderr.Should().Contain("Schema file not found");
        Environment.ExitCode.Should().Be(1);
    }

    [Test]
    public void Handle_SchemaWithAnInvalidName_ReportsTheNameAndReturns1()
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

        var exitCode = 0;
        var stderr = CaptureStderr(() =>
            exitCode = GenerateCommand.Handle(schema, output, "Proj", null, false)
        );

        exitCode.Should().Be(1);
        stderr.Should().Contain("group of operation 'ListPlayers'");
        stderr.Should().NotContain(" at Trax.Cli", "the refusal is a message, not a stack trace");
        output.Exists.Should().BeFalse();
    }

    [Test]
    public void Invoke_WhenHandleFails_ReturnsExitCode1()
    {
        var command = GenerateCommand.Create();
        var schemaPath = Path.Combine(_tempDir, "no-such-file.graphql");

        var exitCode = 0;
        CaptureStderr(() =>
            exitCode = command
                .Parse(new[] { "--schema", schemaPath, "--output", _tempDir + "/o", "--name", "P" })
                .Invoke()
        );

        exitCode.Should().Be(1, "Program returns this value as the process exit code");
    }

    [Test]
    public void Handle_HappyPathGraphQL_ParsesAndGenerates()
    {
        using var hive = TemplateHive.Install();

        var schema = new FileInfo(FixturePath("simple.graphql"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "happy-graphql"));

        var stdout = CaptureStdout(() =>
            GenerateCommand.Handle(schema, output, "HappyGraphQL", null, false, hive.Generator())
        );

        Environment.ExitCode.Should().Be(0);
        stdout.Should().Contain("from graphql schema");
        stdout.Should().Contain("Generated Trax project at:");
        stdout.Should().Contain("Next steps");
        Directory.Exists(Path.Combine(output.FullName, "HappyGraphQL.Hub")).Should().BeTrue();
        Directory.Exists(Path.Combine(output.FullName, "HappyGraphQL.Trains")).Should().BeTrue();
    }

    [Test]
    public void Handle_HappyPathOpenApi_ParsesAndGenerates()
    {
        using var hive = TemplateHive.Install();

        var schema = new FileInfo(FixturePath("petstore.json"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "happy-openapi"));

        var stdout = CaptureStdout(() =>
            GenerateCommand.Handle(schema, output, "HappyOpenApi", null, false, hive.Generator())
        );

        Environment.ExitCode.Should().Be(0);
        stdout.Should().Contain("from openapi schema");
        stdout.Should().Contain("Generated Trax project at:");
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    private static string CaptureStderr(Action action)
    {
        var originalErr = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(originalErr);
        }
        return writer.ToString();
    }

    private static string CaptureStdout(Action action)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
        return writer.ToString();
    }
}
