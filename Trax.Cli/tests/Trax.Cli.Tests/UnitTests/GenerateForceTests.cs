using FluentAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax generate --force</c> replaces an existing output directory. It builds the new project beside it and
/// swaps it in only once every step has succeeded, so a failed run leaves what was there; and it refuses a
/// directory it could not safely replace at all.
///
/// <para>Enforces cli/0004 (<c>docs/adr/0004-generate-force-replaces-only-on-success.md</c>).</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0004-generate-force-replaces-only-on-success.md")]
public class GenerateForceTests
{
    private const string Adr =
        "see cli/0004 (docs/adr/0004-generate-force-replaces-only-on-success.md)";

    private string _root = null!;
    private int _originalExitCode;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-cli-force-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _originalExitCode = Environment.ExitCode;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        Environment.ExitCode = _originalExitCode;
    }

    [Test]
    public void Handle_Force_WhenScaffoldingFails_LeavesTheExistingDirectoryUntouched()
    {
        var output = ExistingOutput(out var sentinel);

        var exitCode = 0;
        var stderr = CaptureStderr(() =>
            exitCode = GenerateCommand.Handle(
                SchemaFile(),
                new DirectoryInfo(output),
                "Proj",
                null,
                force: true,
                new TraxProjectGenerator(FailingScaffold)
            )
        );

        exitCode.Should().Be(1);
        stderr.Should().Contain("template is not installed");
        File.ReadAllText(sentinel)
            .Should()
            .Be("keep me", "a failed run must not delete anything; " + Adr);
    }

    [Test]
    public void Generate_WhenScaffoldingFails_LeavesNothingBesideTheOutput()
    {
        var output = ExistingOutput(out _);
        var generator = new TraxProjectGenerator(FailingScaffold);

        var act = () => generator.Generate(Schema(), output, "Proj", force: true);

        act.Should().Throw<InvalidOperationException>();
        Directory
            .GetFileSystemEntries(_root)
            .Should()
            .BeEquivalentTo([output], "the staging directory is removed after a failure; " + Adr);
    }

    [Test]
    public void Generate_WhenTheOutputDoesNotExist_AndScaffoldingFails_CreatesNothing()
    {
        var output = Path.Combine(_root, "out");
        var generator = new TraxProjectGenerator(FailingScaffold);

        var act = () => generator.Generate(Schema(), output, "Proj", force: false);

        act.Should().Throw<InvalidOperationException>();
        Directory.GetFileSystemEntries(_root).Should().BeEmpty();
    }

    [Test]
    public void Generate_Force_OnSuccess_ReplacesTheDirectory()
    {
        var output = ExistingOutput(out var sentinel);
        var generator = new TraxProjectGenerator(FakeScaffold);

        generator.Generate(Schema(), output, "Proj", force: true);

        File.Exists(sentinel).Should().BeFalse("--force replaces the directory on success");
        File.Exists(Path.Combine(output, "Proj.Hub", "Proj.Hub.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(output, "Proj.Trains", "Proj.Trains.csproj")).Should().BeTrue();
        File.ReadAllText(Path.Combine(output, "Proj.Hub", "Proj.Hub.csproj"))
            .Should()
            .Contain(@"..\Proj.Trains\Proj.Trains.csproj");
        Directory.GetFileSystemEntries(_root).Should().BeEquivalentTo([output]);
    }

    [Test]
    public void Generate_Force_OnADirectoryHoldingAGitRepository_IsRefused()
    {
        var output = ExistingOutput(out var sentinel);
        Directory.CreateDirectory(Path.Combine(output, ".git"));
        var scaffolded = false;
        var generator = new TraxProjectGenerator((_, _) => scaffolded = true);

        var act = () => generator.Generate(Schema(), output, "Proj", force: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*.git*", Adr);
        scaffolded.Should().BeFalse("the refusal comes before any work; " + Adr);
        File.Exists(sentinel).Should().BeTrue();
    }

    [Test]
    public void Generate_Force_OnAGitWorktree_IsRefused()
    {
        // A linked worktree or submodule has a .git file, not a directory.
        var output = ExistingOutput(out var sentinel);
        File.WriteAllText(Path.Combine(output, ".git"), "gitdir: /elsewhere");
        var generator = new TraxProjectGenerator(FakeScaffold);

        var act = () => generator.Generate(Schema(), output, "Proj", force: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*.git*", Adr);
        File.Exists(sentinel).Should().BeTrue();
    }

    [Test]
    public void EnsureReplaceable_TheCurrentDirectory_IsRefused()
    {
        var act = () => TraxProjectGenerator.EnsureReplaceable(_root, currentDirectory: _root);

        act.Should().Throw<InvalidOperationException>().WithMessage("*current directory*", Adr);
    }

    [Test]
    public void EnsureReplaceable_AParentOfTheCurrentDirectory_IsRefused()
    {
        var child = Path.Combine(_root, "a", "b");

        var act = () => TraxProjectGenerator.EnsureReplaceable(_root, currentDirectory: child);

        act.Should().Throw<InvalidOperationException>().WithMessage("*current directory*", Adr);
    }

    [Test]
    public void EnsureReplaceable_ASiblingWithACommonPrefix_IsAllowed()
    {
        // "/tmp/x/out" is not a parent of "/tmp/x/out-other", although the string is a prefix.
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);

        var act = () =>
            TraxProjectGenerator.EnsureReplaceable(
                output,
                currentDirectory: Path.Combine(_root, "out-other")
            );

        act.Should().NotThrow();
    }

    private string ExistingOutput(out string sentinel)
    {
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        sentinel = Path.Combine(output, "keep.txt");
        File.WriteAllText(sentinel, "keep me");
        return output;
    }

    [Test]
    public void Generate_Force_OnADirectoryWhoseSubdirectoryIsAGitRepository_IsRefused()
    {
        // A workspace of side-by-side repositories: the directory itself is not a repository.
        var workspace = Path.Combine(_root, "workspace");
        var repoGit = Path.Combine(workspace, "SomeRepo", ".git");
        Directory.CreateDirectory(repoGit);
        File.WriteAllText(Path.Combine(repoGit, "HEAD"), "ref: refs/heads/main\n");

        var act = () =>
            new TraxProjectGenerator(FakeScaffold).Generate(
                Schema(),
                workspace,
                "Proj",
                force: true
            );

        act.Should().Throw<InvalidOperationException>();
        Directory
            .Exists(repoGit)
            .Should()
            .BeTrue("replacing the directory deletes every repository inside it; " + Adr);
    }

    private static void FailingScaffold(string name, string dir) =>
        throw new InvalidOperationException("The 'trax-hub' template is not installed.");

    private static void FakeScaffold(string name, string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), "<Project>\n</Project>\n");
        File.WriteAllText(
            Path.Combine(dir, "Program.cs"),
            "builder.Services.AddTrax(t => t.AddMediator(typeof(Program).Assembly));\n"
        );
    }

    private static FileInfo SchemaFile() =>
        new(
            Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Fixtures",
                "Schemas",
                "simple.graphql"
            )
        );

    private static Trax.Cli.Models.ApiSchema Schema() =>
        new GraphQLSchemaParser().Parse(SchemaFile().FullName);

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
