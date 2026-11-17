using Trax.Core.Extensions;

namespace Trax.Core.Tests.Meta.Tests;

/// <summary>
/// The analyzer profile is the vendor's default rule set with every reported rule an error, and
/// nothing in this folder opts out of it.
///
/// <para>
/// The root <c>.globalconfig</c> is generated from the configuration Meziantou.Analyzer ships.
/// A hand edit, a <c>#pragma</c>, a <c>NoWarn</c> or a nearer configuration file each turn a rule
/// off for one place, which is how a rule set stops being the same everywhere.
/// </para>
///
/// <para>Enforces <c>Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md"
)]
[TestFixture]
public partial class AnalyzerProfileTests
{
    private const string Adr =
        "Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md";

    private const string Regenerate = ".github/scripts/generate-analyzer-profile.sh";

    [Test]
    public void Profile_IsTheVendorDefault_WithEveryReportedRuleAnError()
    {
        var vendor = Severities(File.ReadAllLines(VendorDefaults()));
        var profileLines = File.ReadAllLines(MonorepoRoot.Combine(".globalconfig"));
        var profile = Severities(profileLines);
        var labelled = LabelledRules(profileLines);

        var problems = new List<string>();

        foreach (var (rule, vendorSeverity) in vendor.OrderBy(v => v.Key, StringComparer.Ordinal))
        {
            if (!profile.TryGetValue(rule, out var severity))
            {
                problems.Add($"{rule}: missing from the profile");
                continue;
            }

            var expected =
                labelled.TryGetValue(rule, out var label)
                    ? (string.Equals(label, "Added", StringComparison.Ordinal) ? "error" : "none")
                : vendorSeverity is "none" or "silent" ? vendorSeverity
                : "error";

            if (!string.Equals(severity, expected, StringComparison.Ordinal))
                problems.Add(
                    $"{rule}: {severity}, expected {expected} (the package ships it as {vendorSeverity})"
                );
        }

        problems.AddRange(
            profile
                .Keys.Where(rule => !vendor.ContainsKey(rule))
                .Order(StringComparer.Ordinal)
                .Select(rule => $"{rule}: not a rule of the pinned package")
        );

        problems
            .Should()
            .BeEmpty(
                "the profile is generated, not edited: every rule Meziantou.Analyzer reports by "
                    + "default is an error, and a rule departs from that only where the generator names it "
                    + $"with its reason. Run {Regenerate}. See {Adr}. Problems:\n  "
                    + string.Join("\n  ", problems)
            );
    }

    [Test]
    public void Sources_DoNotSuppressAProfileRule()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("src", "tests"))
        {
            // self-skip; this file spells the suppressions out in its own test cases
            if (file.EndsWith("AnalyzerProfileTests.cs", StringComparison.Ordinal))
                continue;

            if (Suppression.IsMatch(File.ReadAllText(file)))
                offenders.Add(RepoRoot.Relative(file));
        }

        foreach (var file in BuildFiles())
        {
            if (NoWarn.IsMatch(File.ReadAllText(file)))
                offenders.Add(RepoRoot.Relative(file));
        }

        offenders.AddRange(
            NearerConfigurations()
                .Where(file => RuleSeverity.IsMatch(File.ReadAllText(file)))
                .Select(RepoRoot.Relative)
        );

        offenders
            .Should()
            .BeEmpty(
                "a rule of the analyzer profile is fixed where it fires, not switched off for "
                    + "one place: no #pragma, [SuppressMessage], NoWarn or nearer configuration "
                    + $"file may name one. See {Adr}. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [TestCase("#pragma warning disable MA0004")]
    [TestCase("#pragma warning disable CS1591, MA0048")]
    [TestCase("[SuppressMessage(\"Design\", \"MA0051:Method is too long\")]")]
    [TestCase("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Usage\", \"MA0004\")]")]
    public void Suppression_FlagsEveryFormASourceFileCanTake(string source) =>
        Suppression.IsMatch(source).Should().BeTrue();

    [TestCase("#pragma warning disable CS1591")]
    [TestCase("// MA0004 is why this awaits with ConfigureAwait(false)")]
    public void Suppression_LeavesOtherTextAlone(string source) =>
        Suppression.IsMatch(source).Should().BeFalse();

    private static string VendorDefaults()
    {
        var pin = XDocument
            .Load(MonorepoRoot.Combine("Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Single(e =>
                string.Equals(
                    (string?)e.Attribute("Include"),
                    "Meziantou.Analyzer",
                    StringComparison.Ordinal
                )
            );

        var packages =
            Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages"
            );

        return Path.Combine(
            packages,
            "meziantou.analyzer",
            (string)pin.Attribute("Version").NotNull(),
            "configuration",
            "default.editorconfig"
        );
    }

    private static Dictionary<string, string> Severities(IEnumerable<string> lines) =>
        lines
            .Select(line => RuleSeverity.Match(line))
            .Where(match => match.Success)
            .ToDictionary(
                match => match.Groups["rule"].Value,
                match => match.Groups["severity"].Value,
                StringComparer.Ordinal
            );

    // A rule the generator set on purpose carries its label and reason on the line above:
    // Exempt and Deferred turn a rule off, Added turns on one the package leaves off.
    private static Dictionary<string, string> LabelledRules(string[] lines)
    {
        var labelled = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 1; i < lines.Length; i++)
        {
            var match = RuleSeverity.Match(lines[i]);
            var label = Label.Match(lines[i - 1]);
            if (match.Success && label.Success)
                labelled[match.Groups["rule"].Value] = label.Groups["label"].Value;
        }

        return labelled;
    }

    private static IEnumerable<string> BuildFiles() =>
        SourceFiles
            .Projects()
            .Concat(
                Directory
                    .EnumerateFiles(RepoRoot.Path, "Directory.Build.*", SearchOption.AllDirectories)
                    .Where(IsNotBuildOutput)
            );

    private static IEnumerable<string> NearerConfigurations() =>
        new[] { ".editorconfig", ".globalconfig" }
            .SelectMany(name =>
                Directory.EnumerateFiles(RepoRoot.Path, name, SearchOption.AllDirectories)
            )
            .Where(IsNotBuildOutput);

    private static bool IsNotBuildOutput(string path)
    {
        var s = Path.DirectorySeparatorChar;
        return !path.Contains($"{s}bin{s}", StringComparison.Ordinal)
            && !path.Contains($"{s}obj{s}", StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"^dotnet_diagnostic\.(?<rule>MA\d+)\.severity\s*=\s*(?<severity>\w+)\s*$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex RuleSeverity { get; }

    [GeneratedRegex(
        @"^# (?<label>Exempt|Deferred|Added): \S",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex Label { get; }

    [GeneratedRegex(
        @"#pragma\s+warning\s+disable[^\r\n]*\bMA\d{4}\b|SuppressMessage\s*\([^)]*\bMA\d{4}\b",
        RegexOptions.None,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex Suppression { get; }

    [GeneratedRegex(
        @"<(?:NoWarn|WarningsNotAsErrors)>[^<]*\bMA\d{4}\b",
        RegexOptions.None,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex NoWarn { get; }
}
