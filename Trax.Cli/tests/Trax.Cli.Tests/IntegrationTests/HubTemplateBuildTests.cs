using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
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
public partial class HubTemplateBuildTests
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
        generator.GenerateTrainsLibrary(
            new GraphQLSchemaParser().Parse(FixturePath("simple.graphql")),
            Path.Combine(output, "Demo.Trains"),
            "Demo"
        );
        TraxProjectGenerator.AddProjectReference(hubDir, "Demo.Hub", "Demo.Trains");
        TraxProjectGenerator.PatchProgramCs(hubDir, "Demo");
        SupplyTheTemplatesPackageVersions(output, hubDir);

        var build = Dotnet(output, BuildTimeout, "build", hubDir, "-nologo");

        build.ExitCode.Should().Be(0, $"the generated hub compiles:\n{Errors(build.Output)}");
    }

    // The published template's csproj names its Trax packages without a Version (it relies on the Samples
    // repo's central package management), so outside that repo it fails restore with NU1015, which is
    // tracked separately in Trax.Samples. Until the template ships versions, float them here so the build
    // reaches the compiler, which is what this test is about. Directory.Build.targets is evaluated after
    // the project's items, so Update applies to them.
    private static void SupplyTheTemplatesPackageVersions(string output, string hubDir)
    {
        var csproj = File.ReadAllText(Path.Combine(hubDir, "Demo.Hub.csproj"));
        var unversioned = UnversionedReference()
            .Matches(csproj)
            .Select(m => m.Groups["id"].Value)
            .ToList();
        if (unversioned.Count == 0)
            return;

        var updates = string.Join(
            "\n",
            unversioned.Select(id => $"    <PackageReference Update=\"{id}\" Version=\"1.*\" />")
        );
        File.WriteAllText(
            Path.Combine(output, "Directory.Build.targets"),
            $"""
            <Project>
              <ItemGroup Condition="'$(MSBuildProjectName)' == 'Demo.Hub'">
            {updates}
              </ItemGroup>
            </Project>
            """
        );
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

    [GeneratedRegex("<PackageReference\\s+Include=\"(?<id>[^\"]+)\"\\s*/>")]
    private static partial Regex UnversionedReference();
}
