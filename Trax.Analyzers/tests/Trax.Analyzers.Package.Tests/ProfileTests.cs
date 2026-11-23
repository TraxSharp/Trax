using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Trax.Analyzers.Package.Tests;

/// <summary>
/// rules.txt is the one place a rule is turned on, off or down, and the profile the package ships
/// says exactly what it says.
/// </summary>
[TestFixture]
public partial class ProfileTests
{
    private const string Level = "1";

    [Test]
    public void RulesTxt_NamesEveryRuleTheAnalyzerShips_AndNoOther()
    {
        var shipped = ShippedRuleIds();
        var listed = RulesFile
            .Read()
            .Select(r => r.Id)
            .Where(id => id.StartsWith("TRX", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        shipped
            .Except(listed)
            .Should()
            .BeEmpty(
                "every rule the analyzer ships needs a line in Trax.Analyzers/rules.txt, so a rule an "
                    + "upstream sync brings in is a decision, not a default"
            );
        listed
            .Except(shipped)
            .Should()
            .BeEmpty("rules.txt must not list a rule the analyzer does not ship");
    }

    [Test]
    public void RulesTxt_ListsEachRuleOnce() =>
        RulesFile
            .Read()
            .GroupBy(r => r.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should()
            .BeEmpty();

    [TestCase("src")]
    [TestCase("tests")]
    public void Profile_SaysWhatRulesTxtSays(string scope)
    {
        var expected = RulesFile
            .Read()
            .ToDictionary(r => r.Id, r => scope == "src" ? r.Src : r.Tests, StringComparer.Ordinal);

        var text = File.ReadAllText(Folder.Profile(Level, scope));
        var actual = Severity()
            .Matches(text)
            .ToDictionary(
                m => m.Groups["id"].Value,
                m => m.Groups["severity"].Value,
                StringComparer.Ordinal
            );

        actual
            .Should()
            .Equal(
                expected,
                "the profile is generated from rules.txt; run Trax.Analyzers/tools/generate-profile.sh"
            );
        text.Should().Contain("is_global = true").And.Contain("global_level = -1");
    }

    [Test]
    public void Profile_HoldsNothingButRuleSeverities()
    {
        foreach (var scope in new[] { "src", "tests" })
        {
            File.ReadAllLines(Folder.Profile(Level, scope))
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Where(line => line is not "is_global = true" and not "global_level = -1")
                .Where(line => !Severity().IsMatch(line))
                .Should()
                .BeEmpty(
                    "a bulk setting (dotnet_analyzer_diagnostic.*) or anything else in the "
                        + "profile would change rules rules.txt does not mention"
                );
        }
    }

    private static HashSet<string> ShippedRuleIds()
    {
        var assembly = Assembly.Load("Trax.Analyzers");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract))
        {
            if (typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            {
                var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(type)!;
                ids.UnionWith(analyzer.SupportedDiagnostics.Select(d => d.Id));
            }
        }

        return ids;
    }

    [GeneratedRegex(
        @"^dotnet_diagnostic\.(?<id>[A-Z]+[0-9]+)\.severity = (?<severity>\w+)$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex Severity();
}
