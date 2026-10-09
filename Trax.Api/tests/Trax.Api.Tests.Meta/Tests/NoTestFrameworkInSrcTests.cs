namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// Test frameworks stay out of shipped libraries. A <c>src/</c> project that references NUnit
/// can carry a <c>[Test]</c> into a package a consumer installs, where it is dead weight at
/// best and a second, unrunnable test suite at worst.
///
/// <para>
/// The exception is deliberate and is the reason this guard names names rather than banning
/// the reference outright: the <c>Trax.*.Testing</c> packages exist to ship architecture-guard
/// fixtures. A consumer references one, subclasses the fixture and inherits its
/// <c>[Test]</c> methods, so the attributes have to be in the shipped assembly. Those
/// projects are listed below; anything else is a mistake.
/// </para>
///
/// <para>
/// CsCheck, the property-based testing library, has no such exception. A property test is a test,
/// and the <c>Trax.*.Testing</c> packages ship fixtures, not properties, so only a project that sets
/// <c>IsTestProject</c> may reference it, wherever in the folder it lives.
/// </para>
///
/// <para>Enforces <c>Trax.Docs/adr/0011-test-frameworks-stay-out-of-shipped-libraries.md</c>, and
/// <c>Trax.Docs/adr/0044-property-tests-use-cscheck-in-test-projects-only.md</c> for CsCheck.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0011-test-frameworks-stay-out-of-shipped-libraries.md")]
[Property("adr", "Trax.Docs/adr/0044-property-tests-use-cscheck-in-test-projects-only.md")]
[TestFixture]
public class NoTestFrameworkInSrcTests
{
    private const string PropertyTestsAdr =
        "Trax.Docs/adr/0044-property-tests-use-cscheck-in-test-projects-only.md";

    /// <summary>Test-only packages no project may reference unless it is a test project.</summary>
    private static readonly string[] TestProjectOnlyPackages = ["CsCheck"];

    /// <summary>
    /// Projects allowed to reference a test framework because shipping fixtures is their
    /// product. Adding an entry means a new package whose purpose is to deliver guards.
    /// </summary>
    private static readonly string[] SanctionedTestingPackages =
    [
        "Trax.Core.Testing",
        "Trax.Effect.Data.Testing",
        "Trax.Mediator.Testing",
        "Trax.Api.GraphQL.Testing",
    ];

    private static readonly string[] TestFrameworkPackages =
    [
        "NUnit",
        "NUnit3TestAdapter",
        "xunit",
        "xunit.runner.visualstudio",
        "MSTest.TestFramework",
        "Microsoft.NET.Test.Sdk",
    ];

    private static bool IsSanctioned(string projectPath) =>
        SanctionedTestingPackages.Contains(
            Path.GetFileNameWithoutExtension(projectPath),
            StringComparer.Ordinal
        );

    [Test]
    public void SrcProjects_DoNotReferenceATestFramework_UnlessTheyShipFixtures()
    {
        var src = RepoRoot.Combine("src");
        if (!Directory.Exists(src))
            Assert.Ignore("this repo ships no src/ projects");

        var offenders = new List<string>();

        foreach (var proj in Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsSanctioned(proj))
                continue;

            var referenced = XDocument
                .Load(proj)
                .Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => v is not null)
                .Where(v => TestFrameworkPackages.Contains(v, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (referenced.Count > 0)
                offenders.Add($"{RepoRoot.Relative(proj)} -> {string.Join(", ", referenced)}");
        }

        offenders
            .Should()
            .BeEmpty(
                "a shipped library must not reference a test framework. Move the tests to a "
                    + "project under tests/, or, if the project's purpose is to ship guard "
                    + "fixtures for consumers, add it to SanctionedTestingPackages and say why. "
                    + "Enforces Trax.Docs/adr/0011-test-frameworks-stay-out-of-shipped-libraries.md. "
                    + "Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void SanctionedTestingPackages_AreNotStale()
    {
        var src = RepoRoot.Combine("src");
        if (!Directory.Exists(src))
            Assert.Ignore("this repo ships no src/ projects");

        var present = Directory
            .EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        var claimedHere = SanctionedTestingPackages.Where(present.Contains).ToList();

        foreach (var name in claimedHere)
        {
            var proj = Directory
                .EnumerateFiles(src, $"{name}.csproj", SearchOption.AllDirectories)
                .Single();

            XDocument
                .Load(proj)
                .Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Should()
                .Contain(
                    v => TestFrameworkPackages.Contains(v!, StringComparer.OrdinalIgnoreCase),
                    $"'{name}' is on the sanctioned list because it ships test fixtures. It no "
                        + "longer references a test framework, so the exemption is stale and "
                        + "should be removed."
                );
        }
    }

    [Test]
    public void OnlyTestProjects_ReferenceCsCheck()
    {
        var projects = Directory
            .EnumerateFiles(RepoRoot.Path, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsBuildOutput(p));

        var offenders = TestOnlyReferencesOutsideTestProjects(projects);

        offenders
            .Should()
            .BeEmpty(
                "CsCheck is a test-only dependency: only a project with "
                    + "<IsTestProject>true</IsTestProject> may reference it, and never a shipped "
                    + "package or a Trax.*.Testing package. Move the property tests to a test project. "
                    + $"Enforces {PropertyTestsAdr}. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void OnlyTestProjects_ReferenceCsCheck_FlagsALibraryThatDoes()
    {
        var dir = Directory.CreateTempSubdirectory("trax-cscheck-guard-");
        try
        {
            var library = Path.Combine(dir.FullName, "Library.csproj");
            File.WriteAllText(
                library,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup><PackageReference Include="CsCheck" /></ItemGroup>
                </Project>
                """
            );
            var tests = Path.Combine(dir.FullName, "Library.Tests.csproj");
            File.WriteAllText(
                tests,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>
                  <ItemGroup><PackageReference Include="CsCheck" /></ItemGroup>
                </Project>
                """
            );

            TestOnlyReferencesOutsideTestProjects([library, tests])
                .Should()
                .ContainSingle(
                    $"a library referencing CsCheck is the violation {PropertyTestsAdr} names, "
                        + "and a test project referencing it is the use it allows"
                )
                .Which.Should()
                .Contain("Library.csproj -> CsCheck");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static List<string> TestOnlyReferencesOutsideTestProjects(IEnumerable<string> projects)
    {
        var offenders = new List<string>();

        foreach (var proj in projects)
        {
            var document = XDocument.Load(proj);
            var isTestProject = document
                .Descendants("IsTestProject")
                .Any(e =>
                    string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                );
            if (isTestProject)
                continue;

            var referenced = document
                .Descendants("PackageReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => v is not null)
                .Where(v => TestProjectOnlyPackages.Contains(v, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (referenced.Count > 0)
                offenders.Add($"{RepoRoot.Relative(proj)} -> {string.Join(", ", referenced)}");
        }

        return offenders;
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules");
}
