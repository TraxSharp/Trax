namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// The feature-coverage table in the root <c>README.md</c> names, for each major Trax feature, the
/// test class that proves it end to end. A row naming a class in this repository must point at a
/// file under <c>tests/</c> that declares that class, so a renamed or deleted test cannot leave the
/// table claiming coverage that is gone. Every E2E project under <c>tests/</c> must be named by at
/// least one row, so a sample cannot be added without saying which feature it proves.
///
/// <para>A row may also link a sample's feature file, <c>samples/&lt;Sample&gt;/features/</c> or <c>specs/</c>, with
/// the file name as the link text. The file must exist and declare a <c>Feature:</c>, and an E2E project that
/// compiles it as a <c>ReqnrollFeatureFiles</c> item counts as named by that row, since its tests are the
/// file's scenarios.</para>
///
/// <para>A row naming a class in another Trax repo is checked for shape only (a known repo, a
/// path under <c>tests/</c>, a file named after the class). Its existence is not checked: the
/// sibling repo is not in this checkout in CI, and a local sibling sits on whatever branch its
/// developer left it on, so the answer would depend on something outside this repository.</para>
///
/// <para>Enforces <c>docs/adr/0006-one-sample-per-major-feature-proven-end-to-end.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-one-sample-per-major-feature-proven-end-to-end.md")]
[TestFixture]
public class FeatureCoverageTableTests
{
    private const string Adr = "docs/adr/0006-one-sample-per-major-feature-proven-end-to-end.md";
    private const string Heading = "## Feature coverage";
    private const string NoE2E = "no E2E";
    private const string ThisRepo = "Trax.Samples";

    private static readonly HashSet<string> KnownRepos = new(StringComparer.Ordinal)
    {
        "Trax.Samples",
        "Trax.Core",
        "Trax.Effect",
        "Trax.Mediator",
        "Trax.Scheduler",
        "Trax.Api",
        "Trax.Cli",
    };

    private static readonly Regex TestLink = new(
        @"\[`(?<class>[^`]+)`\]\(https://github\.com/TraxSharp/Trax/blob/main/(?<repo>Trax\.[\w.]+?)/(?<path>[^)\s]+)\)",
        RegexOptions.Compiled
    );

    private static readonly Regex FeatureLink = new(
        @"\[`(?<file>[^`]+\.feature)`\]\(https://github\.com/TraxSharp/Trax/blob/main/Trax\.Samples/(?<path>[^)\s]+)\)",
        RegexOptions.Compiled
    );

    private static readonly Regex FeaturePath = new(
        @"^samples/[^/]+/(?:features|specs)/(?:[^/]+/)*[^/]+\.feature$",
        RegexOptions.Compiled
    );

    private static readonly Regex FeatureKeyword = new(@"^\s*Feature:", RegexOptions.Multiline);

    private sealed record Row(int Line, string Feature, string ProvedBy);

    private sealed record FeatureClaim(int Line, string File, string Path);

    private sealed record Claim(int Line, string Feature, string Class, string Repo, string Path);

    private static List<Row> Rows()
    {
        var lines = File.ReadAllLines(RepoRoot.Combine("README.md"));
        var start = Array.FindIndex(lines, l => l.Trim() == Heading);
        if (start < 0)
            return new List<Row>();

        var rows = new List<Row>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
                break;
            if (!line.StartsWith('|'))
                continue;

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (
                cells.Length < 3
                || cells[1] == "Feature"
                || cells.All(c => c.All(ch => ch is '-' or ':'))
            )
                continue;
            rows.Add(new Row(i + 1, cells[1], cells[^1]));
        }
        return rows;
    }

    private static List<Claim> Claims() =>
        Rows()
            .SelectMany(r =>
                TestLink
                    .Matches(r.ProvedBy)
                    // A feature file's link is a FeatureClaim, checked on its own.
                    .Where(m => !FeatureLink.IsMatch(m.Value))
                    .Select(m => new Claim(
                        r.Line,
                        r.Feature,
                        m.Groups["class"].Value,
                        m.Groups["repo"].Value,
                        m.Groups["path"].Value
                    ))
            )
            .ToList();

    private static List<FeatureClaim> FeatureClaims() =>
        Rows()
            .SelectMany(r =>
                FeatureLink
                    .Matches(r.ProvedBy)
                    .Select(m => new FeatureClaim(
                        r.Line,
                        m.Groups["file"].Value,
                        m.Groups["path"].Value
                    ))
            )
            .ToList();

    /// <summary>
    /// What is wrong with each feature-file link: a path outside a sample's <c>features/</c> or <c>specs/</c>, link
    /// text that is not the file's name, a file that does not exist, or one that declares no <c>Feature:</c>.
    /// </summary>
    private static List<string> FeatureLinkProblems(IEnumerable<FeatureClaim> claims)
    {
        var problems = new List<string>();
        foreach (var claim in claims)
        {
            var at = $"README.md line {claim.Line}: {claim.File} -> {claim.Path}";
            var file = RepoRoot.Combine(claim.Path.Split('/'));
            if (
                !FeaturePath.IsMatch(claim.Path)
                || claim.Path.Split('/').Any(segment => segment is "." or "..")
            )
                problems.Add($"{at} is not under samples/<Sample>/features/ or specs/");
            else if (Path.GetFileName(claim.Path) != claim.File)
                problems.Add($"{at}: the link text is not the file's name");
            else if (!File.Exists(file))
                problems.Add($"{at} does not exist");
            else if (!FeatureKeyword.IsMatch(File.ReadAllText(file)))
                problems.Add($"{at} declares no Feature:");
        }
        return problems;
    }

    /// <summary>The full path of every feature file a project compiles through <c>ReqnrollFeatureFiles</c>.</summary>
    private static IEnumerable<string> CompiledFeatureFiles(string projectDir) =>
        Directory
            .EnumerateFiles(projectDir, "*.csproj")
            .SelectMany(csproj => XDocument.Load(csproj).Descendants("ReqnrollFeatureFiles"))
            .SelectMany(item => ((string?)item.Attribute("Include") ?? "").Split(';'))
            .Where(include => include.Trim().Length > 0)
            .SelectMany<string, string>(include =>
            {
                var full = Path.GetFullPath(
                    Path.Combine(projectDir, include.Trim().Replace('\\', '/'))
                );
                var dir = Path.GetDirectoryName(full)!;
                var name = Path.GetFileName(full);
                if (!name.Contains('*') && !name.Contains('?'))
                    return [full];
                return Directory.Exists(dir)
                    ? Directory.EnumerateFiles(dir, name).Select(Path.GetFullPath)
                    : [];
            });

    [Test]
    public void TheReadmeHasAFeatureCoverageTable() =>
        Rows()
            .Should()
            .NotBeEmpty(
                $"README.md must carry a '{Heading}' section with a table of features and the "
                    + $"test classes that prove them. See {Adr}."
            );

    [Test]
    public void EveryRow_NamesATestClassOrSaysThereIsNone()
    {
        var offenders = Rows()
            .Where(r =>
                !TestLink.IsMatch(r.ProvedBy)
                && !FeatureLink.IsMatch(r.ProvedBy)
                && !r.ProvedBy.Contains(NoE2E, StringComparison.Ordinal)
            )
            .Select(r => $"README.md line {r.Line}: {r.Feature}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "each feature-coverage row must link at least one test class as "
                    + "[`ClassTests`](https://github.com/TraxSharp/Trax/blob/main/<Folder>/tests/...) or "
                    + "feature file as [`name.feature`](https://github.com/TraxSharp/Trax/blob/main/"
                    + "Trax.Samples/samples/<Sample>/features/name.feature), or say "
                    + $"'{NoE2E}' when no end-to-end test exists anywhere. See {Adr}:\n"
                    + string.Join("\n", offenders)
            );
    }

    [Test]
    public void EveryLink_HasTheShapeOfATestClassInATraxRepo()
    {
        var offenders = Claims()
            .Where(c =>
                !KnownRepos.Contains(c.Repo)
                || !c.Path.StartsWith("tests/", StringComparison.Ordinal)
                || !c.Class.EndsWith("Tests", StringComparison.Ordinal)
                || Path.GetFileName(c.Path) != c.Class + ".cs"
            )
            .Select(c => $"README.md line {c.Line}: {c.Class} -> {c.Repo}/{c.Path}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a feature-coverage link names a class ending in 'Tests', in a Trax repo, at "
                    + "tests/.../<Class>.cs, so a reader can find it and this guard can check it. "
                    + $"See {Adr}:\n"
                    + string.Join("\n", offenders)
            );
    }

    [Test]
    public void EveryClassNamedForThisRepo_IsDeclaredInTheLinkedFile()
    {
        var claims = Claims().Where(c => c.Repo == ThisRepo).ToList();
        claims.Should().NotBeEmpty($"the table names no class in this repo. See {Adr}.");

        var offenders = new List<string>();
        foreach (var claim in claims)
        {
            var file = RepoRoot.Combine(claim.Path.Split('/'));
            if (!File.Exists(file))
            {
                offenders.Add($"README.md line {claim.Line}: {claim.Path} does not exist");
                continue;
            }

            var code = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            if (!Regex.IsMatch(code, $@"\bclass\s+{Regex.Escape(claim.Class)}\b"))
                offenders.Add(
                    $"README.md line {claim.Line}: {claim.Path} declares no class {claim.Class}"
                );
        }

        offenders
            .Should()
            .BeEmpty(
                "every test class the feature-coverage table names in this repo must exist where "
                    + "the row links it; fix the row when a test moves or is renamed, and drop the "
                    + $"feature's claim only with a replacement. See {Adr}:\n"
                    + string.Join("\n", offenders)
            );
    }

    [Test]
    public void EveryFeatureFileLinked_ExistsInASample()
    {
        var claims = FeatureClaims();
        claims
            .Should()
            .NotBeEmpty(
                $"the table links no feature file; the Bakery's row links its own. See {Adr}."
            );

        var problems = FeatureLinkProblems(claims);
        problems
            .Should()
            .BeEmpty(
                "a feature-coverage link to a feature file names the file, under the sample's features/ "
                    + "or specs/ folder, and the file declares a Feature:. Fix the row when a feature file "
                    + $"moves or is renamed. See {Adr}:\n"
                    + string.Join("\n", problems)
            );
    }

    [Test]
    public void AFeatureLink_ToAMissingFileOrOutsideASamplesFeatures_IsAProblem()
    {
        FeatureLinkProblems([new(1, "shop.feature", "samples/Bakery/features/shop.feature")])
            .Should()
            .BeEmpty("the Bakery's shop.feature exists and declares a Feature:");
        FeatureLinkProblems([new(1, "gone.feature", "samples/Bakery/features/gone.feature")])
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("does not exist");
        FeatureLinkProblems([new(1, "shop.feature", "samples/Bakery/features/ordering.feature")])
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("the link text is not the file's name");
        FeatureLinkProblems([new(1, "shop.feature", "tests/Trax.Samples.Bakery.E2E/shop.feature")])
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("is not under samples/<Sample>/features/ or specs/");
    }

    [Test]
    public void EveryE2EProject_IsNamedByARow()
    {
        var linked = Claims()
            .Where(c => c.Repo == ThisRepo)
            .Select(c => c.Path.Split('/')[1])
            .ToHashSet(StringComparer.Ordinal);
        var linkedFeatures = FeatureClaims()
            .Select(c => Path.GetFullPath(RepoRoot.Combine(c.Path.Split('/'))))
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = Directory
            .EnumerateDirectories(RepoRoot.Combine("tests"))
            // A folder a retired project left behind (bin/ and obj/ only) is not a suite.
            .Where(dir => Directory.EnumerateFiles(dir, "*.csproj").Any())
            .Where(dir => Path.GetFileName(dir).EndsWith(".E2E", StringComparison.Ordinal))
            .Where(dir => !linked.Contains(Path.GetFileName(dir)))
            .Where(dir => !CompiledFeatureFiles(dir).Any(linkedFeatures.Contains))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unlisted
            .Should()
            .BeEmpty(
                "every E2E suite proves a feature, so the feature-coverage table must name at least "
                    + "one of its classes, or link a feature file it compiles as ReqnrollFeatureFiles. "
                    + $"See {Adr}. Unlisted:\n"
                    + string.Join("\n", unlisted)
            );
    }
}
