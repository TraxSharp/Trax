namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// The feature-coverage table in the root <c>README.md</c> names, for each major Trax feature, the
/// test class that proves it end to end. A row naming a class in this repository must point at a
/// file under <c>tests/</c> that declares that class, so a renamed or deleted test cannot leave the
/// table claiming coverage that is gone. Every E2E project under <c>tests/</c> must be named by at
/// least one row, so a sample cannot be added without saying which feature it proves.
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
        "Trax.Dashboard",
        "Trax.Cli",
    };

    private static readonly Regex TestLink = new(
        @"\[`(?<class>[^`]+)`\]\(https://github\.com/TraxSharp/(?<repo>[\w.]+)/blob/main/(?<path>[^)\s]+)\)",
        RegexOptions.Compiled
    );

    private sealed record Row(int Line, string Feature, string ProvedBy);

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
                    .Select(m => new Claim(
                        r.Line,
                        r.Feature,
                        m.Groups["class"].Value,
                        m.Groups["repo"].Value,
                        m.Groups["path"].Value
                    ))
            )
            .ToList();

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
                && !r.ProvedBy.Contains(NoE2E, StringComparison.Ordinal)
            )
            .Select(r => $"README.md line {r.Line}: {r.Feature}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "each feature-coverage row must link at least one test class as "
                    + "[`ClassTests`](https://github.com/TraxSharp/<Repo>/blob/main/tests/...), or say "
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
    public void EveryE2EProject_IsNamedByARow()
    {
        var linked = Claims()
            .Where(c => c.Repo == ThisRepo)
            .Select(c => c.Path.Split('/')[1])
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = Directory
            .EnumerateDirectories(RepoRoot.Combine("tests"))
            // A folder a retired project left behind (bin/ and obj/ only) is not a suite.
            .Where(dir => Directory.EnumerateFiles(dir, "*.csproj").Any())
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.EndsWith(".E2E", StringComparison.Ordinal))
            .Where(name => !linked.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unlisted
            .Should()
            .BeEmpty(
                "every E2E suite proves a feature, so the feature-coverage table must name at least "
                    + $"one of its classes. See {Adr}. Unlisted:\n"
                    + string.Join("\n", unlisted)
            );
    }
}
