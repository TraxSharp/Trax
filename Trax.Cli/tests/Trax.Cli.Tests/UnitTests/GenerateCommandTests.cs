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

    [Test]
    public void Handle_HappyPathGraphQL_ParsesAndGenerates()
    {
        if (!IsTraxHubTemplateInstalled())
            Assert.Ignore("trax-hub template is not installed (Trax.Samples.Templates).");

        var schema = new FileInfo(FixturePath("simple.graphql"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "happy-graphql"));

        var stdout = CaptureStdout(() =>
            GenerateCommand.Handle(schema, output, "HappyGraphQL", null, false)
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
        if (!IsTraxHubTemplateInstalled())
            Assert.Ignore("trax-hub template is not installed (Trax.Samples.Templates).");

        var schema = new FileInfo(FixturePath("petstore.json"));
        var output = new DirectoryInfo(Path.Combine(_tempDir, "happy-openapi"));

        var stdout = CaptureStdout(() =>
            GenerateCommand.Handle(schema, output, "HappyOpenApi", null, false)
        );

        Environment.ExitCode.Should().Be(0);
        stdout.Should().Contain("from openapi schema");
        stdout.Should().Contain("Generated Trax project at:");
    }

    private static bool IsTraxHubTemplateInstalled()
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", "new list trax-hub")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return stdout.Contains("trax-hub");
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
