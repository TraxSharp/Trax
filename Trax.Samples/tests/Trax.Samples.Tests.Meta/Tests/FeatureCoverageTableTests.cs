using System.Globalization;

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
public partial class FeatureCoverageTableTests
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

    private sealed record Row(int Line, string Feature, string ProvedBy);

    private sealed record FeatureClaim(int Line, string File, string Path);

    private sealed record Claim(int Line, string Feature, string Class, string Repo, string Path);

    private static async Task<List<Row>> RowsAsync()
    {
        var lines = await File.ReadAllLinesAsync(RepoRoot.Combine("README.md"))
            .ConfigureAwait(false);
        var start = Array.FindIndex(
            lines,
            l => string.Equals(l.Trim(), Heading, StringComparison.Ordinal)
        );
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
                || string.Equals(cells[1], "Feature", StringComparison.Ordinal)
                || cells.All(c => c.All(ch => ch is '-' or ':'))
            )
                continue;
            rows.Add(new Row(i + 1, cells[1], cells[^1]));
        }
        return rows;
    }

    private static async Task<List<Claim>> ClaimsAsync() =>
        (await RowsAsync().ConfigureAwait(false))
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

    private static async Task<List<FeatureClaim>> FeatureClaimsAsync() =>
        (await RowsAsync().ConfigureAwait(false))
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
    private static async Task<List<string>> FeatureLinkProblemsAsync(
        IEnumerable<FeatureClaim> claims
    )
    {
        var problems = new List<string>();
        foreach (var claim in claims)
        {
            var at = string.Create(
                CultureInfo.InvariantCulture,
                $"README.md line {claim.Line}: {claim.File} -> {claim.Path}"
            );
            var file = RepoRoot.Combine(claim.Path.Split('/'));
            if (
                !FeaturePath.IsMatch(claim.Path)
                || claim.Path.Split('/').Any(segment => segment is "." or "..")
            )
                problems.Add($"{at} is not under samples/<Sample>/features/ or specs/");
            else if (
                !string.Equals(Path.GetFileName(claim.Path), claim.File, StringComparison.Ordinal)
            )
                problems.Add($"{at}: the link text is not the file's name");
            else if (!File.Exists(file))
                problems.Add($"{at} does not exist");
            else if (
                !FeatureKeyword.IsMatch(await File.ReadAllTextAsync(file).ConfigureAwait(false))
            )
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
                if (
                    !name.Contains('*', StringComparison.Ordinal)
                    && !name.Contains('?', StringComparison.Ordinal)
                )
                    return [full];
                return Directory.Exists(dir)
                    ? Directory.EnumerateFiles(dir, name).Select(Path.GetFullPath)
                    : [];
            });

    [Test]
    public async Task TheReadmeHasAFeatureCoverageTable() =>
        (await RowsAsync().ConfigureAwait(false))
            .Should()
            .NotBeEmpty(
                $"README.md must carry a '{Heading}' section with a table of features and the "
                    + $"test classes that prove them. See {Adr}."
            );

    [Test]
    public async Task EveryRow_NamesATestClassOrSaysThereIsNone()
    {
        var offenders = (await RowsAsync().ConfigureAwait(false))
            .Where(r =>
                !TestLink.IsMatch(r.ProvedBy)
                && !FeatureLink.IsMatch(r.ProvedBy)
                && !r.ProvedBy.Contains(NoE2E, StringComparison.Ordinal)
            )
            .Select(r =>
                string.Create(CultureInfo.InvariantCulture, $"README.md line {r.Line}: {r.Feature}")
            )
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "each feature-coverage row must link at least one test class as "
                    + "[`ClassTests`](https://github.com/TraxSharp/Trax/blob/main/<Folder>/tests/...) or "
                    + "feature file as [`name.feature`](https://github.com/TraxSharp/Trax/blob/main/"
                    + "Trax.Samples/samples/<Sample>/features/name.feature), or say "
                    + $"'{NoE2E}' when no end-to-end test exists anywhere. See {Adr}:\n"
                    + string.Join('\n', offenders)
            );
    }

    [Test]
    public async Task EveryLink_HasTheShapeOfATestClassInATraxRepo()
    {
        var offenders = (await ClaimsAsync().ConfigureAwait(false))
            .Where(c =>
                !KnownRepos.Contains(c.Repo)
                || !c.Path.StartsWith("tests/", StringComparison.Ordinal)
                || !c.Class.EndsWith("Tests", StringComparison.Ordinal)
                || !string.Equals(
                    Path.GetFileName(c.Path),
                    c.Class + ".cs",
                    StringComparison.Ordinal
                )
            )
            .Select(c =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"README.md line {c.Line}: {c.Class} -> {c.Repo}/{c.Path}"
                )
            )
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a feature-coverage link names a class ending in 'Tests', in a Trax repo, at "
                    + "tests/.../<Class>.cs, so a reader can find it and this guard can check it. "
                    + $"See {Adr}:\n"
                    + string.Join('\n', offenders)
            );
    }

    [Test]
    public async Task EveryClassNamedForThisRepo_IsDeclaredInTheLinkedFile()
    {
        var claims = (await ClaimsAsync().ConfigureAwait(false))
            .Where(c => string.Equals(c.Repo, ThisRepo, StringComparison.Ordinal))
            .ToList();
        claims.Should().NotBeEmpty($"the table names no class in this repo. See {Adr}.");

        var offenders = new List<string>();
        foreach (var claim in claims)
        {
            var file = RepoRoot.Combine(claim.Path.Split('/'));
            if (!File.Exists(file))
            {
                offenders.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"README.md line {claim.Line}: {claim.Path} does not exist"
                    )
                );
                continue;
            }

            var code = SourceText.StripCommentsAndStrings(
                await File.ReadAllTextAsync(file).ConfigureAwait(false)
            );
            if (
                !Regex.IsMatch(
                    code,
                    $@"\bclass\s+{Regex.Escape(claim.Class)}\b",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                )
            )
                offenders.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"README.md line {claim.Line}: {claim.Path} declares no class {claim.Class}"
                    )
                );
        }

        offenders
            .Should()
            .BeEmpty(
                "every test class the feature-coverage table names in this repo must exist where "
                    + "the row links it; fix the row when a test moves or is renamed, and drop the "
                    + $"feature's claim only with a replacement. See {Adr}:\n"
                    + string.Join('\n', offenders)
            );
    }

    [Test]
    public async Task EveryFeatureFileLinked_ExistsInASample()
    {
        var claims = await FeatureClaimsAsync().ConfigureAwait(false);
        claims
            .Should()
            .NotBeEmpty(
                $"the table links no feature file; the CheckIn sample's row links its own. See {Adr}."
            );

        var problems = await FeatureLinkProblemsAsync(claims).ConfigureAwait(false);
        problems
            .Should()
            .BeEmpty(
                "a feature-coverage link to a feature file names the file, under the sample's features/ "
                    + "or specs/ folder, and the file declares a Feature:. Fix the row when a feature file "
                    + $"moves or is renamed. See {Adr}:\n"
                    + string.Join('\n', problems)
            );
    }

    [Test]
    public async Task AFeatureLink_ToAMissingFileOrOutsideASamplesFeatures_IsAProblem()
    {
        (
            await FeatureLinkProblemsAsync([
                    new(1, "gate.feature", "samples/CheckIn/features/gate.feature"),
                ])
                .ConfigureAwait(false)
        )
            .Should()
            .BeEmpty("the CheckIn sample's gate.feature exists and declares a Feature:");
        (
            await FeatureLinkProblemsAsync([
                    new(1, "gone.feature", "samples/CheckIn/features/gone.feature"),
                ])
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("does not exist");
        (
            await FeatureLinkProblemsAsync([
                    new(1, "gate.feature", "samples/CheckIn/features/checking-in.feature"),
                ])
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("the link text is not the file's name");
        (
            await FeatureLinkProblemsAsync([
                    new(1, "gate.feature", "tests/Trax.Samples.CheckIn.E2E/gate.feature"),
                ])
                .ConfigureAwait(false)
        )
            .Should()
            .ContainSingle()
            .Which.Should()
            .EndWith("is not under samples/<Sample>/features/ or specs/");
    }

    [Test]
    public async Task EveryE2EProject_IsNamedByARow()
    {
        var linked = (await ClaimsAsync().ConfigureAwait(false))
            .Where(c => string.Equals(c.Repo, ThisRepo, StringComparison.Ordinal))
            .Select(c => c.Path.Split('/')[1])
            .ToHashSet(StringComparer.Ordinal);
        var linkedFeatures = (await FeatureClaimsAsync().ConfigureAwait(false))
            .Select(c => Path.GetFullPath(RepoRoot.Combine(c.Path.Split('/'))))
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = Directory
            .EnumerateDirectories(RepoRoot.Combine("tests"))
            .Where(dir =>
                // A folder a retired project left behind (bin/ and obj/ only) is not a suite.
                Directory.EnumerateFiles(dir, "*.csproj").Any()
                && Path.GetFileName(dir).EndsWith(".E2E", StringComparison.Ordinal)
                && !linked.Contains(Path.GetFileName(dir))
                && !CompiledFeatureFiles(dir).Any(linkedFeatures.Contains)
            )
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

        unlisted
            .Should()
            .BeEmpty(
                "every E2E suite proves a feature, so the feature-coverage table must name at least "
                    + "one of its classes, or link a feature file it compiles as ReqnrollFeatureFiles. "
                    + $"See {Adr}. Unlisted:\n"
                    + string.Join('\n', unlisted)
            );
    }

    [GeneratedRegex(@"^\s*Feature:", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FeatureKeyword { get; }

    [GeneratedRegex(
        @"^samples/[^/]+/(?:features|specs)/(?:[^/]+/)*[^/]+\.feature$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex FeaturePath { get; }

    [GeneratedRegex(
        @"\[`(?<file>[^`]+\.feature)`\]\(https://github\.com/TraxSharp/Trax/blob/main/Trax\.Samples/(?<path>[^)\s]+)\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex FeatureLink { get; }

    [GeneratedRegex(
        @"\[`(?<class>[^`]+)`\]\(https://github\.com/TraxSharp/Trax/blob/main/(?<repo>Trax\.[\w.]+?)/(?<path>[^)\s]+)\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex TestLink { get; }
}
