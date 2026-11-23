using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Tests.Fakes;

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

    private string _root = "";
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
    public async Task Handle_Force_WhenScaffoldingFails_LeavesTheExistingDirectoryUntouched()
    {
        var (output, sentinel) = await ExistingOutputAsync().ConfigureAwait(false);

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    GenerateCommand.HandleAsync(
                        SchemaFile(),
                        new DirectoryInfo(output),
                        "Proj",
                        type: null,
                        force: true,
                        o,
                        e,
                        new TraxProjectGenerator(FailingScaffoldAsync)
                    )
            )
            .ConfigureAwait(false);
        var (exitCode, stderr) = (run.ExitCode, run.Stderr);

        exitCode.Should().Be(1);
        stderr.Should().Contain("template is not installed");
        (await File.ReadAllTextAsync(sentinel).ConfigureAwait(false))
            .Should()
            .Be("keep me", "a failed run must not delete anything; " + Adr);
    }

    [Test]
    public async Task Generate_WhenScaffoldingFails_LeavesNothingBesideTheOutput()
    {
        var (output, _) = await ExistingOutputAsync().ConfigureAwait(false);
        var generator = new TraxProjectGenerator(FailingScaffoldAsync);

        var act = async () =>
            await generator
                .GenerateAsync(
                    await SchemaAsync().ConfigureAwait(false),
                    output,
                    "Proj",
                    force: true
                )
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        Directory
            .GetFileSystemEntries(_root)
            .Should()
            .BeEquivalentTo([output], "the staging directory is removed after a failure; " + Adr);
    }

    [Test]
    public async Task Generate_WhenTheOutputDoesNotExist_AndScaffoldingFails_CreatesNothing()
    {
        var output = Path.Combine(_root, "out");
        var generator = new TraxProjectGenerator(FailingScaffoldAsync);

        var act = async () =>
            await generator
                .GenerateAsync(
                    await SchemaAsync().ConfigureAwait(false),
                    output,
                    "Proj",
                    force: false
                )
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        Directory.GetFileSystemEntries(_root).Should().BeEmpty();
    }

    [Test]
    public async Task Generate_Force_OnSuccess_ReplacesTheDirectory()
    {
        var (output, sentinel) = await ExistingOutputAsync().ConfigureAwait(false);
        var generator = new TraxProjectGenerator(FakeScaffoldAsync);

        await generator
            .GenerateAsync(await SchemaAsync().ConfigureAwait(false), output, "Proj", force: true)
            .ConfigureAwait(false);

        File.Exists(sentinel).Should().BeFalse("--force replaces the directory on success");
        File.Exists(Path.Combine(output, "Proj.Hub", "Proj.Hub.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(output, "Proj.Trains", "Proj.Trains.csproj")).Should().BeTrue();
        (
            await File.ReadAllTextAsync(Path.Combine(output, "Proj.Hub", "Proj.Hub.csproj"))
                .ConfigureAwait(false)
        )
            .Should()
            .Contain(@"..\Proj.Trains\Proj.Trains.csproj");
        Directory.GetFileSystemEntries(_root).Should().BeEquivalentTo([output]);
    }

    [Test]
    public async Task Generate_Force_OnADirectoryHoldingAGitRepository_IsRefused()
    {
        var (output, sentinel) = await ExistingOutputAsync().ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(output, ".git"));
        var scaffolded = false;
        var generator = new TraxProjectGenerator(
            (_, _) =>
            {
                scaffolded = true;
                return Task.CompletedTask;
            }
        );

        var act = async () =>
            await generator
                .GenerateAsync(
                    await SchemaAsync().ConfigureAwait(false),
                    output,
                    "Proj",
                    force: true
                )
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*.git*", Adr);
        scaffolded.Should().BeFalse("the refusal comes before any work; " + Adr);
        File.Exists(sentinel).Should().BeTrue();
    }

    [Test]
    public async Task Generate_Force_OnAGitWorktree_IsRefused()
    {
        // A linked worktree or submodule has a .git file, not a directory.
        var (output, sentinel) = await ExistingOutputAsync().ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, ".git"), "gitdir: /elsewhere")
            .ConfigureAwait(false);
        var generator = new TraxProjectGenerator(FakeScaffoldAsync);

        var act = async () =>
            await generator
                .GenerateAsync(
                    await SchemaAsync().ConfigureAwait(false),
                    output,
                    "Proj",
                    force: true
                )
                .ConfigureAwait(false);

        (
            await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false)
        ).WithMessage("*.git*", Adr);
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

    private async Task<(string Output, string Sentinel)> ExistingOutputAsync()
    {
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "keep me").ConfigureAwait(false);
        return (output, sentinel);
    }

    [Test]
    public async Task Generate_Force_OnADirectoryWhoseSubdirectoryIsAGitRepository_IsRefused()
    {
        // A workspace of side-by-side repositories: the directory itself is not a repository.
        var workspace = Path.Combine(_root, "workspace");
        var repoGit = Path.Combine(workspace, "SomeRepo", ".git");
        Directory.CreateDirectory(repoGit);
        await File.WriteAllTextAsync(Path.Combine(repoGit, "HEAD"), "ref: refs/heads/main\n");

        var act = async () =>
            await new TraxProjectGenerator(FakeScaffoldAsync)
                .GenerateAsync(
                    await SchemaAsync().ConfigureAwait(false),
                    workspace,
                    "Proj",
                    force: true
                )
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        Directory
            .Exists(repoGit)
            .Should()
            .BeTrue("replacing the directory deletes every repository inside it; " + Adr);
    }

    private static Task FailingScaffoldAsync(string name, string dir) =>
        throw new InvalidOperationException("The 'trax-hub' template is not installed.");

    private static async Task FakeScaffoldAsync(string name, string dir)
    {
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.csproj"), "<Project>\n</Project>\n")
            .ConfigureAwait(false);
        // The template ships its Trax pins beside the hub csproj.
        await File.WriteAllTextAsync(
                Path.Combine(dir, "Directory.Packages.props"),
                """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Trax.Effect" Version="1.58.0" />
                    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.58.0" />
                    <PackageVersion Include="Trax.Mediator" Version="1.24.0" />
                    <PackageVersion Include="Trax.Scheduler" Version="1.35.0" />
                  </ItemGroup>
                </Project>
                """
            )
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(dir, "Program.cs"),
                "builder.Services.AddTrax(t => t.AddMediator(typeof(Program).Assembly));\n"
            )
            .ConfigureAwait(false);
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

    private static async Task<Trax.Cli.Models.ApiSchema> SchemaAsync() =>
        await new GraphQLSchemaParser().ParseAsync(SchemaFile().FullName).ConfigureAwait(false);

    [Test]
    public async Task Generate_writes_the_hubs_exact_Trax_versions_into_the_trains_project()
    {
        var output = Path.Combine(_root, "pinned");
        var schema = await new Trax.Cli.Schema.GraphQL.GraphQLSchemaParser()
            .ParseAsync(SchemaFile().FullName)
            .ConfigureAwait(false);

        await new TraxProjectGenerator(FakeScaffoldAsync)
            .GenerateAsync(schema, output, "Demo", force: false)
            .ConfigureAwait(false);

        var csproj = await File.ReadAllTextAsync(
                Path.Combine(output, "Demo.Trains", "Demo.Trains.csproj")
            )
            .ConfigureAwait(false);
        csproj
            .Should()
            .NotContain("*", "a floating version lets the pair resolve different Trax releases");
        csproj.Should().Contain("<PackageReference Include=\"Trax.Effect\" Version=\"1.58.0\" />");
        csproj
            .Should()
            .Contain("<PackageReference Include=\"Trax.Mediator\" Version=\"1.24.0\" />");
        csproj
            .Should()
            .Contain("<PackageReference Include=\"Trax.Scheduler\" Version=\"1.35.0\" />");
    }

    [Test]
    public void HubPackageVersions_falls_back_to_versions_on_the_hubs_own_references()
    {
        var hub = Path.Combine(_root, "old-hub");
        Directory.CreateDirectory(hub);
        File.WriteAllText(
            Path.Combine(hub, "Old.Hub.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Trax.Effect" Version="1.50.0" />
                <PackageReference Include="Trax.Effect.Data.InMemory" Version="1.50.0" />
                <PackageReference Include="Trax.Mediator" Version="1.20.0" />
                <PackageReference Include="Trax.Scheduler" Version="1.30.0" />
              </ItemGroup>
            </Project>
            """
        );

        TraxProjectGenerator
            .HubPackageVersions(hub, "Old.Hub")
            .Should()
            .Contain(new KeyValuePair<string, string>("Trax.Mediator", "1.20.0"));
    }

    [Test]
    public void HubPackageVersions_refuses_a_hub_that_pins_nothing()
    {
        var hub = Path.Combine(_root, "bare-hub");
        Directory.CreateDirectory(hub);
        File.WriteAllText(Path.Combine(hub, "Bare.Hub.csproj"), "<Project>\n</Project>\n");

        var act = () => TraxProjectGenerator.HubPackageVersions(hub, "Bare.Hub");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*pins no version for Trax.Effect*");
    }

    private async Task<string> HubPinningAsync(string name, string mediatorVersion)
    {
        var hub = Path.Combine(_root, name);
        Directory.CreateDirectory(hub);
        await File.WriteAllTextAsync(
                Path.Combine(hub, "Directory.Packages.props"),
                $"""
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Trax.Effect" Version="1.50.0" />
                    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.50.0" />
                    <PackageVersion Include="Trax.Mediator" Version="{mediatorVersion}" />
                    <PackageVersion Include="Trax.Scheduler" Version="[1.30.0, 2.0.0)" />
                  </ItemGroup>
                </Project>
                """
            )
            .ConfigureAwait(false);
        return hub;
    }

    [Test]
    public async Task HubPackageVersions_refuses_a_pin_that_is_not_a_nuget_version()
    {
        // Decoded by the XML reader, &quot; is a quote, which written raw into the trains csproj would
        // close the attribute and let the rest of the value add to the project.
        var hub = await HubPinningAsync("quoted-hub", "1.0.0&quot; Condition=&quot;false")
            .ConfigureAwait(false);

        var act = () => TraxProjectGenerator.HubPackageVersions(hub, "Quoted.Hub");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Trax.Mediator at '1.0.0\" Condition=\"false'*not a NuGet version*");
    }

    [TestCase("1.20.0")]
    [TestCase("1.20.0-beta.1")]
    [TestCase("1.*")]
    [TestCase("[1.20.0]")]
    [TestCase("(1.0,2.0]")]
    public async Task HubPackageVersions_accepts_nuget_versions_and_ranges(string version)
    {
        var hub = await HubPinningAsync($"hub-{Guid.NewGuid():N}", version).ConfigureAwait(false);

        TraxProjectGenerator
            .HubPackageVersions(hub, "Any.Hub")
            .Should()
            .Contain(new KeyValuePair<string, string>("Trax.Mediator", version));
    }

    [Test]
    public async Task HubPackageVersions_reports_a_props_file_it_cannot_open_cleanly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("file modes are Unix-only");
            return;
        }
        var hub = await HubPinningAsync("locked-hub", "1.20.0").ConfigureAwait(false);
        var props = Path.Combine(hub, "Directory.Packages.props");
        File.SetUnixFileMode(props, UnixFileMode.None);
        try
        {
            var act = () => TraxProjectGenerator.HubPackageVersions(hub, "Locked.Hub");

            act.Should().Throw<InvalidOperationException>().WithMessage($"Cannot read {props}*");
        }
        finally
        {
            File.SetUnixFileMode(props, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
