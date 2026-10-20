namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// Enforces the Trax dependency chain (AGENTS.md): a folder may reference a <c>Trax.*</c> package or project
/// only in its own family or an allowed upstream family, never a downstream or parallel one. The map below
/// is the whole chain; the test self-detects which folder it runs in from the <c>.slnx</c> at the folder's
/// .NET root and applies that folder's rule, so the identical file lives in every folder's Meta project.
///
/// <para>A <c>ProjectReference</c> is checked too, by the referenced project's file name, which is the
/// package id the project packs as.</para>
///
/// <para>This catches an upstream folder pulling in a downstream one, for example a test that references
/// Trax.Api.GraphQL from inside Trax.Effect, an Effect project that references a state-machine project, or a
/// state-machine project that references the mediator.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0003-a-repo-depends-only-on-what-is-upstream.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0003-a-repo-depends-only-on-what-is-upstream.md")]
[TestFixture]
public class DependencyDirectionTests
{
    // For each family, the full set of upstream families it may reference. Its own family is always
    // allowed. Core is the root; Effect -> Core; StateMachine -> Effect; Mediator -> StateMachine;
    // Scheduler -> Mediator; Api -> Scheduler; Cli -> Scheduler; Samples sits at the bottom and may
    // reference everything upstream.
    private static readonly IReadOnlyDictionary<string, string[]> AllowedUpstream = new Dictionary<
        string,
        string[]
    >(StringComparer.Ordinal)
    {
        ["Trax.Core"] = Array.Empty<string>(),
        ["Trax.Effect"] = new[] { "Trax.Core" },
        ["Trax.StateMachine"] = new[] { "Trax.Core", "Trax.Effect" },
        ["Trax.Mediator"] = new[] { "Trax.Core", "Trax.Effect", "Trax.StateMachine" },
        ["Trax.Scheduler"] = new[]
        {
            "Trax.Core",
            "Trax.Effect",
            "Trax.StateMachine",
            "Trax.Mediator",
        },
        ["Trax.Api"] = new[]
        {
            "Trax.Core",
            "Trax.Effect",
            "Trax.StateMachine",
            "Trax.Mediator",
            "Trax.Scheduler",
        },
        ["Trax.Cli"] = new[]
        {
            "Trax.Core",
            "Trax.Effect",
            "Trax.StateMachine",
            "Trax.Mediator",
            "Trax.Scheduler",
        },
        ["Trax.Samples"] = new[]
        {
            "Trax.Core",
            "Trax.Effect",
            "Trax.StateMachine",
            "Trax.Mediator",
            "Trax.Scheduler",
            "Trax.Api",
            "Trax.Cli",
        },
    };

    private static readonly string[] Families =
    {
        "Trax.Core",
        "Trax.Effect",
        "Trax.StateMachine",
        "Trax.Mediator",
        "Trax.Scheduler",
        "Trax.Api",
        "Trax.Cli",
        "Trax.Samples",
    };

    [Test]
    public void No_project_references_a_downstream_or_parallel_Trax_package()
    {
        var repo = DetectRepo();
        repo.Should()
            .NotBeNull("the current folder family should be detectable from the .slnx at its root");

        var projects = new List<(string Path, XDocument Project)>();
        foreach (var csproj in SourceFiles.Projects())
        {
            try
            {
                projects.Add((RepoRoot.Relative(csproj), XDocument.Load(csproj)));
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to parse {RepoRoot.Relative(csproj)}: {ex.Message}");
                return;
            }
        }

        Offenders(repo!, projects)
            .Should()
            .BeEmpty(
                $"{repo} may reference only its own family and its allowed upstream "
                    + $"[{string.Join(", ", AllowedUpstream[repo!])}] per the Trax dependency chain. A "
                    + "package or project reference to any other Trax family points the wrong way "
                    + "(downstream or parallel). Offenders:\n  "
                    + string.Join("\n  ", Offenders(repo!, projects))
            );
    }

    [TestCase("Trax.StateMachine", "Trax.Mediator")]
    [TestCase("Trax.StateMachine", "Trax.Mediator.Testing")]
    [TestCase("Trax.Effect", "Trax.StateMachine")]
    [TestCase("Trax.Effect", "Trax.StateMachine.Persistence")]
    public void A_reference_against_the_chain_is_refused(string folder, string referenced)
    {
        Offenders(folder, [("x.csproj", ProjectReferencing(referenced))])
            .Should()
            .ContainSingle(
                $"{folder} is upstream of {referenced}'s family, so referencing it points the wrong way"
            );
    }

    [TestCase("Trax.StateMachine", "Trax.Effect.Data")]
    [TestCase("Trax.Mediator", "Trax.StateMachine.Persistence")]
    public void A_reference_along_the_chain_is_allowed(string folder, string referenced)
    {
        Offenders(folder, [("x.csproj", ProjectReferencing(referenced))])
            .Should()
            .BeEmpty($"{referenced} is in a family upstream of {folder}");
    }

    // Every reference in the given projects to a Trax family the folder may not reach, as
    // "project -> reference [family]".
    private static List<string> Offenders(
        string repo,
        IEnumerable<(string Path, XDocument Project)> projects
    )
    {
        var allowed = new HashSet<string>(AllowedUpstream[repo], StringComparer.Ordinal) { repo };
        var offenders = new List<string>();
        foreach (var (path, project) in projects)
        {
            foreach (var reference in project.Descendants())
            {
                var include = ReferencedName(reference);
                if (include is null || !include.StartsWith("Trax.", StringComparison.Ordinal))
                    continue;

                // A Trax package outside the folder families (e.g. a standalone Trax.Runner.*)
                // is out of scope for this chain guard; skip it rather than flag it.
                var family = FamilyOf(include);
                if (family is null)
                    continue;
                if (!allowed.Contains(family))
                    offenders.Add($"{path} -> {include} [{family}]");
            }
        }
        return offenders;
    }

    private static XDocument ProjectReferencing(string referenced) =>
        XDocument.Parse(
            $"""<Project><ItemGroup><ProjectReference Include="../{referenced}/{referenced}.csproj" /></ItemGroup></Project>"""
        );

    // The package id of a PackageReference, or the project name of a ProjectReference (its file name without
    // the extension, which is the package id the project packs as). Any other element yields null.
    private static string? ReferencedName(XElement reference)
    {
        var include = reference.Attribute("Include")?.Value;
        if (string.IsNullOrEmpty(include))
            return null;
        return reference.Name.LocalName switch
        {
            "PackageReference" => include,
            "ProjectReference" => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')),
            _ => null,
        };
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
