using System.Text.Json;
using System.Xml.Linq;
using AwesomeAssertions;
using Trax.Samples.Templates.Tests.Utils;
using static Trax.Samples.Templates.Tests.Utils.Dotnet;

namespace Trax.Samples.Templates.Tests.IntegrationTests;

/// <summary>
/// A project scaffolded from the packed <c>Trax.Samples.Templates</c> restores on its own,
/// outside this repository. Inside the repo the template projects take their package versions
/// from the root <c>Directory.Packages.props</c> and build Trax from source, neither of which a
/// scaffolded project sees, so the package has to carry the versions with it. The Trax packages
/// come from <see cref="ScaffoldFeed"/>, packed from this commit at the version the templates
/// were packed with.
///
/// <para>Enforces <c>docs/adr/0004-the-template-package-carries-its-package-versions.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0004-the-template-package-carries-its-package-versions.md")]
public class ScaffoldedTemplateRestoreTests
{
    private const string Adr =
        "see docs/adr/0004-the-template-package-carries-its-package-versions.md";

    private string _workDir = null!;
    private string _hive = null!;

    [OneTimeSetUp]
    public async Task PackAndInstallTheTemplates()
    {
        // A directory with no Directory.Packages.props or Directory.Build.props above it, which
        // is where a consumer runs `dotnet new`. Its nuget.config stands in for the packages
        // being published.
        _workDir = Path.Combine(Path.GetTempPath(), "trax-template-restore-" + Guid.NewGuid());
        _hive = Path.Combine(_workDir, "hive");
        Directory.CreateDirectory(_workDir);

        var packed = await ScaffoldFeed.Get();
        ScaffoldFeed.WriteNuGetConfig(_workDir, packed);
        await Run(_workDir, "new", "install", packed.TemplatesNupkg, "--debug:custom-hive", _hive);
    }

    [OneTimeTearDown]
    public void DeleteTheWorkDirectory()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [TestCase("trax-hub")]
    [TestCase("trax-scheduler")]
    [TestCase("trax-api")]
    public async Task A_scaffolded_project_restores_outside_the_repo(string shortName)
    {
        var (output, restore) = await ScaffoldAndRestore(shortName, "restores");

        restore
            .ExitCode.Should()
            .Be(0, $"a scaffolded {shortName} must restore on its own ({Adr}):\n{restore.Output}");
    }

    /// <summary>
    /// Every Trax package a scaffolded project resolves, direct or transitive, is one the
    /// template pins, at exactly the pinned version. A pin on the direct references alone let
    /// Trax.Dashboard pull the Trax.Api packages at the version it was built against, which was
    /// not the version this repo builds and tests the template with.
    /// </summary>
    [TestCase("trax-hub")]
    [TestCase("trax-scheduler")]
    [TestCase("trax-api")]
    public async Task A_scaffolded_project_resolves_only_the_pinned_Trax_versions(string shortName)
    {
        var (output, restore) = await ScaffoldAndRestore(shortName, "pins");
        restore.ExitCode.Should().Be(0, restore.Output);

        var props = XDocument.Load(Path.Combine(output, "Directory.Packages.props"));
        props
            .Descendants("CentralPackageTransitivePinningEnabled")
            .Select(e => e.Value.Trim())
            .Should()
            .Equal(
                new[] { "true" },
                $"without transitive pinning a pin cannot reach a package that arrives transitively ({Adr})"
            );

        var pins = props
            .Descendants("PackageVersion")
            .ToDictionary(
                e => e.Attribute("Include")!.Value,
                e => e.Attribute("Version")!.Value,
                StringComparer.OrdinalIgnoreCase
            );

        using var assets = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(output, "obj", "project.assets.json"))
        );
        var resolvedTrax = assets
            .RootElement.GetProperty("libraries")
            .EnumerateObject()
            .Where(l => l.Value.GetProperty("type").GetString() == "package")
            .Select(l => l.Name.Split('/'))
            .Where(parts => parts[0].StartsWith("Trax.", StringComparison.OrdinalIgnoreCase))
            .Select(parts => (Id: parts[0], Version: parts[1]))
            .ToList();

        resolvedTrax.Should().NotBeEmpty();

        var unpinned = resolvedTrax
            .Where(p => !pins.TryGetValue(p.Id, out var pinned) || pinned != p.Version)
            .Select(p =>
                $"{p.Id} {p.Version} (pinned: {(pins.TryGetValue(p.Id, out var v) ? v : "none")})"
            )
            .ToList();

        unpinned
            .Should()
            .BeEmpty(
                $"a scaffolded {shortName} must resolve every Trax package at the version the "
                    + $"template pins ({Adr})"
            );
    }

    private async Task<(string Output, (int ExitCode, string Output) Restore)> ScaffoldAndRestore(
        string shortName,
        string suffix
    )
    {
        var output = Path.Combine(_workDir, $"{shortName}-{suffix}");
        await Run(
            _workDir,
            "new",
            shortName,
            "--name",
            "Scaffolded",
            "--output",
            output,
            "--debug:custom-hive",
            _hive
        );

        var restore = await Run(output, allowFailure: true, "restore");
        return (output, restore);
    }
}
