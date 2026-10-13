using System.Text.Json.Nodes;

namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Playwright for .NET, which the browser tests use, and Playwright for Node, which the samples' screenshot
/// script uses, are pinned at one version. Each names the browser build it downloads, so two versions would
/// download two chromiums and an upgrade of one would leave the other behind.
///
/// <para>Enforces <c>Trax.Docs/adr/0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md"
)]
[TestFixture]
public class PlaywrightVersionTests
{
    private const string Adr =
        "Trax.Docs/adr/0049-browser-tests-use-playwright-for-dotnet-in-test-projects-only.md";

    [Test]
    public void The_NuGet_and_npm_Playwright_versions_match()
    {
        var nuget = XDocument
            .Load(MonorepoRoot.Combine("Directory.Packages.props"))
            .Descendants("PackageVersion")
            .SingleOrDefault(e => (string?)e.Attribute("Include") == "Microsoft.Playwright")
            ?.Attribute("Version")
            ?.Value;
        var screenshots = JsonNode.Parse(
            File.ReadAllText(RepoRoot.Combine("scripts", "screenshots", "package.json"))
        )!;
        var npm =
            (string?)screenshots["dependencies"]?["playwright"]
            ?? (string?)screenshots["devDependencies"]?["playwright"];

        nuget
            .Should()
            .NotBeNull(
                $"the root Directory.Packages.props pins Microsoft.Playwright. Enforces {Adr}"
            );
        npm.Should()
            .NotBeNull($"scripts/screenshots/package.json depends on playwright. Enforces {Adr}");
        nuget
            .Should()
            .Be(
                npm,
                "the browser tests and the screenshot script must download the same browser build: "
                    + "pin Microsoft.Playwright in the root Directory.Packages.props at the exact version "
                    + $"scripts/screenshots/package.json names, and move both together. Enforces {Adr}"
            );
    }
}
