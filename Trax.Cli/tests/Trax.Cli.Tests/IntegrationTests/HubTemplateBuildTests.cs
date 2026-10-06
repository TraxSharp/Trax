using System.Diagnostics;
using AwesomeAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;

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

    private string _root = null!;
    private string _hive = null!;

    [OneTimeSetUp]
    public void InstallTheTemplateIntoAPrivateHive()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-cli-hub-build-{Guid.NewGuid():N}");
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);

        var install = Dotnet(
            _root,
            InstallTimeout,
            "new",
            "install",
            "Trax.Samples.Templates",
            "--debug:custom-hive",
            _hive
        );
        install.ExitCode.Should().Be(0, $"the template package installs:\n{install.Output}");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void A_generated_hub_builds()
    {
        var output = Path.Combine(_root, "out");
        var hubDir = Path.Combine(output, "Demo.Hub");
        var scaffold = Dotnet(
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
        );
        scaffold.ExitCode.Should().Be(0, scaffold.Output);

        var generator = new TraxProjectGenerator();
        var trainsDir = Path.Combine(output, "Demo.Trains");
        generator.GenerateTrainsLibrary(
            new GraphQLSchemaParser().Parse(FixturePath("simple.graphql")),
            trainsDir,
            "Demo",
            TraxProjectGenerator.HubPackageVersions(hubDir, "Demo.Hub")
        );
        File.ReadAllText(Path.Combine(trainsDir, "Demo.Trains.csproj"))
            .Should()
            .NotContain("*", "the trains library takes the hub's exact Trax versions");
        TraxProjectGenerator.AddProjectReference(hubDir, "Demo.Hub", "Demo.Trains");
        TraxProjectGenerator.PatchProgramCs(hubDir, "Demo");

        var build = Dotnet(output, BuildTimeout, "build", hubDir, "-nologo");

        build.ExitCode.Should().Be(0, $"the generated hub compiles:\n{Errors(build.Output)}");
    }

    private static string Errors(string output) =>
        string.Join(
            "\n",
            output
                .Split('\n')
                .Where(l => l.Contains(" error ", StringComparison.Ordinal))
                .Distinct()
                .Take(20)
        );

    private static (int ExitCode, string Output) Dotnet(
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

        using var process = Process.Start(psi)!;
        // Both streams at once: a build writes more than a pipe buffer.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"dotnet {string.Join(' ', args)} did not finish within {timeout}.");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);
}
