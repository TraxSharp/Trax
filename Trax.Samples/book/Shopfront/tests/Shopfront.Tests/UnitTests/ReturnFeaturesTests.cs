using System.Globalization;
using Shopfront.Trains.ReturnOrder;
using Trax.Core.Extensions;
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
        Trax
            .StateMachine.Gherkin.Lexicon.ReadDirectory(
                Features,
                [typeof(IReturnOrderTrain).Assembly]
            )
            .NotNull();

    private static FeatureGlossary Glossary => Lexicon.GlossaryOrThrow();

    private static async Task<FeatureFile[]> FilesAsync()
    {
        var files = new List<FeatureFile>();
        foreach (
            var path in Directory.GetFiles(Features, "*.feature").Order(StringComparer.Ordinal)
        )
            files.Add(
                new FeatureFile(
                    Path.GetFileName(path),
                    await File.ReadAllTextAsync(path).ConfigureAwait(false)
                )
            );
        return [.. files];
    }

    [Test]
    public async Task EveryScenario_PassesOnTheCommittedMachines()
    {
        var irs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(Machines, "*.ir.json"))
            irs[Path.GetFileName(path)[..^".ir.json".Length]] = await File.ReadAllTextAsync(path)
                .ConfigureAwait(false);

        var report = FeatureMachines.Run(
            Glossary,
            await FilesAsync().ConfigureAwait(false),
            irs,
            await File.ReadAllTextAsync(Path.Combine(Machines, "host.json")).ConfigureAwait(false)
        );

        report.Problems.Should().BeEmpty();
        report
            .Scenarios.Where(s => s.Status != FeatureStatus.Passed)
            .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.File}:{s.Line} {s.Name}"))
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task TheCommittedMachines_AreWhatTheFeaturesDerive()
    {
        var derived = FeatureMachines.Derive(Glossary, await FilesAsync().ConfigureAwait(false));

        derived.Succeeded.Should().BeTrue(string.Join('\n', derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            (await ReadAsync($"{id}.ir.json").ConfigureAwait(false))
                .Should()
                .Be(ir, "run trax machine from-gherkin again");
        (await ReadAsync("host.json").ConfigureAwait(false))
            .Should()
            .Be(derived.Host, "run trax machine from-gherkin again");
        (await ReadAsync("glossary.json").ConfigureAwait(false))
            .Should()
            .Be(Lexicon.Json.NotNull().TrimEnd('\n'), "run trax machine from-gherkin again");
    }

    private static async Task<string> ReadAsync(string file) =>
        (await File.ReadAllTextAsync(Path.Combine(Machines, file)).ConfigureAwait(false))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n');
}
