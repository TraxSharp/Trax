using Shopfront.Trains.ReturnOrder;
using Trax.StateMachine.Gherkin;

namespace Shopfront.Tests.UnitTests;

// The returns features, run in memory on the machines committed in machines/from-features, as
// trax machine run runs them, and those machines and the glossary are what the features and the
// lexicon derive today. The lexicon is read with the train's records, so its Returns: line is
// checked against the train's output here (ADR Trax.Docs/adr/0059).
[TestFixture]
public class ReturnFeaturesTests
{
    private static string Features => Path.Combine(AppContext.BaseDirectory, "features");

    private static string Machines => Path.Combine(AppContext.BaseDirectory, "machines");

    private static LexiconReading Lexicon =>
        Trax.StateMachine.Gherkin.Lexicon.ReadDirectory(
            Features,
            [typeof(IReturnOrderTrain).Assembly]
        )!;

    private static FeatureGlossary Glossary => Lexicon.GlossaryOrThrow();

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
        Read("glossary.json")
            .Should()
            .Be(Lexicon.Json!.TrimEnd('\n'), "run trax machine from-gherkin again");
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(Machines, file)).Replace("\r\n", "\n").TrimEnd('\n');
}
