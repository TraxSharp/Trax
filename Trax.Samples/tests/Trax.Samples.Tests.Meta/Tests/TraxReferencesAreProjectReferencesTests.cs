namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Inside the repository a Trax package is referenced as a project, never as a package. A
/// <c>PackageReference</c> to one would build against whichever release nuget.org has rather than
/// the code in the same commit, and a change to the package would stop reaching the project that
/// uses it. The template content under <c>templates/content/</c> is the one exception: it is
/// scaffolded outside the repository, and its references are swapped for project references at
/// build time.
///
/// <para>Enforces <c>Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md")]
[TestFixture]
public class TraxReferencesAreProjectReferencesTests
{
    private const string Adr =
        "Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md";

    [Test]
    public void No_project_references_a_Trax_package_built_in_this_repository()
    {
        var builtHere = TraxProjects();
        builtHere.Should().NotBeEmpty("the repository builds the Trax packages it references");

        var offenders = new List<string>();
        foreach (var csproj in SourceFiles.Projects().Where(p => !IsTemplateContent(p)))
        {
            foreach (var pkg in XDocument.Load(csproj).Descendants("PackageReference"))
            {
                var include = pkg.Attribute("Include")?.Value;
                if (include is not null && builtHere.Contains(include))
                    offenders.Add($"{RepoRoot.Relative(csproj)} -> {include}");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "a Trax package built in this repository is referenced with a ProjectReference to "
                    + "its csproj, so every build compiles against the same commit. Offenders "
                    + $"({Adr}):\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    /// <summary>
    /// The package id of every Trax project in the repository: the file name of each csproj under
    /// a folder's <c>src/</c>.
    /// </summary>
    private static HashSet<string> TraxProjects() =>
        Directory
            .EnumerateDirectories(MonorepoRoot.Path, "Trax.*")
            .Select(folder => Path.Combine(folder, "src"))
            .Where(Directory.Exists)
            .SelectMany(src =>
                Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            )
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsTemplateContent(string path) =>
        path.Replace('\\', '/').Contains("/templates/content/", StringComparison.Ordinal);
}
