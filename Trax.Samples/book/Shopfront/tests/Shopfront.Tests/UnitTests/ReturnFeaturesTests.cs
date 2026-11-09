using Trax.StateMachine.Gherkin;

namespace Shopfront.Tests.UnitTests;

// The returns features, run in memory on the machines committed in machines/from-features, as
// trax machine run runs them, and those machines are what the features derive today.
[TestFixture]
public class ReturnFeaturesTests
{
    private static string Features => Path.Combine(AppContext.BaseDirectory, "features");

    private static string Machines => Path.Combine(AppContext.BaseDirectory, "machines");

    private static FeatureGlossary Glossary =>
        FeatureGlossary.Read(File.ReadAllText(Path.Combine(Features, "glossary.json")));

    private static FeatureFile[] Files =>
        [
            .. Directory
                .GetFiles(Features, "*.feature")
                .Order(StringComparer.Ordinal)
                .Select(p => new FeatureFile(Path.GetFileName(p), File.ReadAllText(p))),
        ];

    [Test]
    public void EveryScenario_PassesOnTheCommittedMachines()
    {
        var irs = Directory
            .GetFiles(Machines, "*.ir.json")
            .ToDictionary(p => Path.GetFileName(p)[..^".ir.json".Length], File.ReadAllText);

        var report = FeatureMachines.Run(
            Glossary,
            Files,
            irs,
            File.ReadAllText(Path.Combine(Machines, "host.json"))
        );

        report.Problems.Should().BeEmpty();
        report
            .Scenarios.Where(s => s.Status != FeatureStatus.Passed)
            .Select(s => $"{s.File}:{s.Line} {s.Name}")
            .Should()
            .BeEmpty();
    }

    [Test]
    public void TheCommittedMachines_AreWhatTheFeaturesDerive()
    {
        var derived = FeatureMachines.Derive(Glossary, Files);

        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            Read($"{id}.ir.json").Should().Be(ir, "run trax machine from-gherkin again");
        Read("host.json").Should().Be(derived.Host, "run trax machine from-gherkin again");
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(Machines, file)).Replace("\r\n", "\n").TrimEnd('\n');
}
