using System.CommandLine;
using FluentAssertions;
using Trax.Cli.Commands;

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
    public void Handle_UnsupportedExplicitType_Throws()
    {
        var schema = new FileInfo(Path.Combine(_tempDir, "schema.graphql"));
        File.WriteAllText(schema.FullName, "type Query { hi: String }");
        var output = new DirectoryInfo(Path.Combine(_tempDir, "out"));

        Action act = () => GenerateCommand.Handle(schema, output, "Proj", "wat", false);

        act.Should().Throw<Exception>();
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
}
