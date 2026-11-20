using System.Diagnostics;
using AwesomeAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// Scaffolds a hub from the published <c>Trax.Samples.Templates</c> package and builds it, with the trains
/// library generated from a schema beside it. The template is installed into a private hive
/// (<c>--debug:custom-hive</c>), so this runs everywhere, CI included, without touching the machine's templates,
/// and it catches the template changing under the Program.cs patch, which a toy Program.cs cannot.
///
/// <para>It repeats <see cref="TraxProjectGenerator.Generate"/>'s steps with the hive passed to
/// <c>dotnet new</c>, because <c>Generate</c> always uses the machine's templates.</para>
/// </summary>
[TestFixture]
public class HubTemplateBuildTests
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(8);

    private string _root = "";
    private string _hive = "";

    [OneTimeSetUp]
    public async Task InstallTheTemplateIntoAPrivateHive()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-cli-hub-build-{Guid.NewGuid():N}");
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);

        var install = await DotnetAsync(
                _root,
                InstallTimeout,
                "new",
                "install",
                "Trax.Samples.Templates",
                "--debug:custom-hive",
                _hive
            )
            .ConfigureAwait(false);
        install.ExitCode.Should().Be(0, $"the template package installs:\n{install.Output}");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task A_generated_hub_builds()
    {
        var output = Path.Combine(_root, "out");
        var hubDir = Path.Combine(output, "Demo.Hub");
        var scaffold = await DotnetAsync(
                _root,
                InstallTimeout,
                "new",
                "trax-hub",
                "-n",
                "Demo.Hub",
                "-o",
                hubDir,
                "--debug:custom-hive",
                _hive
            )
            .ConfigureAwait(false);
        scaffold.ExitCode.Should().Be(0, scaffold.Output);

        var generator = new TraxProjectGenerator();
        var trainsDir = Path.Combine(output, "Demo.Trains");
        await generator
            .GenerateTrainsLibraryAsync(
                await new GraphQLSchemaParser()
                    .ParseAsync(FixturePath("simple.graphql"))
                    .ConfigureAwait(false),
                trainsDir,
                "Demo",
                TraxProjectGenerator.HubPackageVersions(hubDir, "Demo.Hub")
            )
            .ConfigureAwait(false);
        (
            await File.ReadAllTextAsync(Path.Combine(trainsDir, "Demo.Trains.csproj"))
                .ConfigureAwait(false)
        )
            .Should()
            .NotContain("*", "the trains library takes the hub's exact Trax versions");
        await TraxProjectGenerator
            .AddProjectReferenceAsync(hubDir, "Demo.Hub", "Demo.Trains")
            .ConfigureAwait(false);
        await TraxProjectGenerator.PatchProgramCsAsync(hubDir, "Demo").ConfigureAwait(false);

        var build = await DotnetAsync(output, BuildTimeout, "build", hubDir, "-nologo")
            .ConfigureAwait(false);

        build.ExitCode.Should().Be(0, $"the generated hub compiles:\n{Errors(build.Output)}");
    }

    private static string Errors(string output) =>
        string.Join(
            '\n',
            output
                .Split('\n')
                .Where(l => l.Contains(" error ", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Take(20)
        );

    private static async Task<(int ExitCode, string Output)> DotnetAsync(
        string workingDirectory,
        TimeSpan timeout,
        params string[] args
    )
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi).NotNull();
        // Both streams at once: a build writes more than a pipe buffer.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"dotnet {string.Join(' ', args)} did not finish within {timeout}.");
        }
        return (
            process.ExitCode,
            await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false)
        );
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);
}
