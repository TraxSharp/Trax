using AwesomeAssertions;
using Trax.Samples.CheckIn.Trains;
using Trax.StateMachine.Gherkin;

namespace Trax.Samples.CheckIn.E2E;

/// <summary>
/// The check-in features, run on its committed machines in memory, as <c>trax machine run</c> runs them, and the
/// committed machines and glossary are what the features and the lexicon derive today, as
/// <c>trax machine from-gherkin --check</c> checks. The lexicon is read with the train's records, so its
/// <c>Returns:</c> line is checked against <c>BoardingPassOutput</c> here, before the host's startup check would (ADR <c>Trax.Docs/adr/0059</c>).
/// </summary>
public class InMemoryRunTests
{
    private static string Features => Path.Combine(AppContext.BaseDirectory, "features");

    private static string Machines => Path.Combine(AppContext.BaseDirectory, "machines");

    private static LexiconReading Lexicon =>
        Trax.StateMachine.Gherkin.Lexicon.ReadDirectory(
            Features,
            [typeof(IIssueBoardingPassTrain).Assembly]
        )!;

    private static FeatureGlossary Glossary =>
        Lexicon.Glossary
        ?? throw new InvalidOperationException(string.Join("\n", Lexicon.Problems));

    private static FeatureFile[] Files =>
        Directory
            .GetFiles(Features, "*.feature")
            .Order(StringComparer.Ordinal)
            .Select(p => new FeatureFile(Path.GetFileName(p), File.ReadAllText(p)))
            .ToArray();

    [Test]
    public void Every_scenario_passes_on_the_committed_machines()
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
            .Select(s =>
                $"{s.File}:{s.Line} {s.Name}: {s.Steps.FirstOrDefault(x => x.Status == FeatureStatus.Failed)?.Message}"
            )
            .Should()
            .BeEmpty();
        report.Scenarios.Should().HaveCountGreaterThan(10);
    }

    [Test]
    public void The_committed_machines_are_what_the_features_derive()
    {
        var derived = FeatureMachines.Derive(Glossary, Files);

        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            Lf(File.ReadAllText(Path.Combine(Machines, id + ".ir.json")))
                .Should()
                .Be(ir + "\n", $"{id}.ir.json is stale: run trax machine from-gherkin");
        Lf(File.ReadAllText(Path.Combine(Machines, "host.json"))).Should().Be(derived.Host + "\n");
        Lf(File.ReadAllText(Path.Combine(Machines, "access.txt"))).Should().Be(derived.Access);
        Lf(File.ReadAllText(Path.Combine(Machines, "glossary.json")))
            .Should()
            .Be(Lexicon.Json, "glossary.json is stale: run trax machine from-gherkin");
    }

    [Test]
    public void Nothing_the_glossary_names_is_left_out_of_the_features() =>
        FeatureMachines.Missing(Glossary, Files).Should().BeEmpty();

    // A checkout with CRLF line endings holds the same files, as trax machine from-gherkin --check reads them.
    private static string Lf(string text) => text.Replace("\r\n", "\n");
}
