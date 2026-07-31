namespace Trax.Scheduler.Tests.Meta.Tests;

/// <summary>
/// Enforces the Trax dependency chain (CLAUDE.md): a repo may take a <c>Trax.*</c> <c>PackageReference</c>
/// only on its own family or an allowed upstream family, never a downstream or parallel one. The map below
/// is the whole chain; the test self-detects which repo it runs in from the <c>.slnx</c> at the repo root
/// and applies that repo's rule, so the identical file lives in every repo's Meta project. Intra-repo
/// dependencies use <c>ProjectReference</c> and are exempt (they are not PackageReferences).
///
/// <para>This catches an upstream repo pulling in a downstream one, for example a test that references
/// Trax.Api.GraphQL from inside Trax.Effect. That only compiles against the locally-packed feed and hides a
/// real layering inversion. Sibling <see cref="CrossRepoPackageReferenceTests"/> checks that a legitimate
/// cross-repo reference is centrally managed; this one checks it points the right way in the first place.</para>
/// </summary>
[TestFixture]
public class DependencyDirectionTests
{
    // For each repo family, the full set of upstream families it may PackageReference. Its own family is
    // always allowed. Empirically matches the current graph and the chain: Core is the root; Effect -> Core;
    // Mediator -> Effect; Scheduler -> Mediator; Api -> Scheduler; Dashboard -> Api; Cli -> Scheduler;
    // Samples sits at the bottom and may reference everything upstream.
    private static readonly IReadOnlyDictionary<string, string[]> AllowedUpstream = new Dictionary<
        string,
        string[]
    >(StringComparer.Ordinal)
    {
        ["Trax.Core"] = Array.Empty<string>(),
        ["Trax.Effect"] = new[] { "Trax.Core" },
        ["Trax.Mediator"] = new[] { "Trax.Core", "Trax.Effect" },
        ["Trax.Scheduler"] = new[] { "Trax.Core", "Trax.Effect", "Trax.Mediator" },
        ["Trax.Api"] = new[] { "Trax.Core", "Trax.Effect", "Trax.Mediator", "Trax.Scheduler" },
        ["Trax.Dashboard"] = new[]
        {
            "Trax.Core",
            "Trax.Effect",
            "Trax.Mediator",
            "Trax.Scheduler",
            "Trax.Api",
        },
        ["Trax.Cli"] = new[] { "Trax.Core", "Trax.Effect", "Trax.Mediator", "Trax.Scheduler" },
        ["Trax.Samples"] = new[]
        {
            "Trax.Core",
            "Trax.Effect",
            "Trax.Mediator",
            "Trax.Scheduler",
            "Trax.Dashboard",
            "Trax.Api",
            "Trax.Cli",
        },
    };

    private static readonly string[] Families =
    {
        "Trax.Core",
        "Trax.Effect",
        "Trax.Mediator",
        "Trax.Scheduler",
        "Trax.Dashboard",
        "Trax.Api",
        "Trax.Cli",
        "Trax.Samples",
    };

    [Test]
    public void No_project_references_a_downstream_or_parallel_Trax_package()
    {
        var repo = DetectRepo();
        repo.Should()
            .NotBeNull(
                "the current repo family should be detectable from the .slnx at the repo root"
            );

        var allowed = new HashSet<string>(AllowedUpstream[repo!], StringComparer.Ordinal) { repo! };
        var offenders = new List<string>();

        foreach (var csproj in SourceFiles.Projects())
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(csproj);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to parse {RepoRoot.Relative(csproj)}: {ex.Message}");
                return;
            }

            foreach (var pkg in doc.Descendants("PackageReference"))
            {
                var include = pkg.Attribute("Include")?.Value;
                if (
                    string.IsNullOrEmpty(include)
                    || !include.StartsWith("Trax.", StringComparison.Ordinal)
                )
                    continue;

                // A Trax package outside the eight core-repo families (e.g. a standalone Trax.Runner.*)
                // is out of scope for this chain guard; skip it rather than flag it.
                var family = FamilyOf(include);
                if (family is null)
                    continue;
                if (!allowed.Contains(family))
                    offenders.Add($"{RepoRoot.Relative(csproj)} -> {include} [{family}]");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                $"{repo} may PackageReference only its own family and its allowed upstream "
                    + $"[{string.Join(", ", AllowedUpstream[repo!])}] per the Trax dependency chain. A "
                    + "reference to any other Trax family points the wrong way (downstream or parallel); use "
                    + "a ProjectReference for intra-repo dependencies. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static string? FamilyOf(string package) =>
        Families.FirstOrDefault(f =>
            package.Equals(f, StringComparison.Ordinal)
            || package.StartsWith(f + ".", StringComparison.Ordinal)
        );

    private static string? DetectRepo()
    {
        var slnx = Directory
            .EnumerateFiles(RepoRoot.Path, "*.slnx", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (slnx is null)
            return null;
        var name = Path.GetFileNameWithoutExtension(slnx);
        return AllowedUpstream.ContainsKey(name) ? name : null;
    }
}
