using System.Globalization;
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

    private static FeatureGlossary Glossary => Lexicon.GlossaryOrThrow();

    private static async Task<FeatureFile[]> FilesAsync()
    {
        var files = new List<FeatureFile>();
        foreach (var p in Directory.GetFiles(Features, "*.feature").Order(StringComparer.Ordinal))
            files.Add(
                new FeatureFile(
                    Path.GetFileName(p),
                    await File.ReadAllTextAsync(p).ConfigureAwait(false)
                )
            );
        return [.. files];
    }

    private static Task<string> ReadAsync(params string[] path) =>
        File.ReadAllTextAsync(Path.Combine(path));

    [Test]
    public async Task Every_scenario_passes_on_the_committed_machines()
    {
        var irs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Directory.GetFiles(Machines, "*.ir.json"))
            irs[Path.GetFileName(p)[..^".ir.json".Length]] = await ReadAsync(p)
                .ConfigureAwait(false);

        var report = FeatureMachines.Run(
            Glossary,
            await FilesAsync().ConfigureAwait(false),
            irs,
            await ReadAsync(Machines, "host.json").ConfigureAwait(false)
        );

        report.Problems.Should().BeEmpty();
        report
            .Scenarios.Where(s => s.Status != FeatureStatus.Passed)
            .Select(s =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{s.File}:{s.Line} {s.Name}: {s.Steps.FirstOrDefault(x => x.Status == FeatureStatus.Failed)?.Message}"
                )
            )
            .Should()
            .BeEmpty();
        report.Scenarios.Should().HaveCountGreaterThan(10);
    }

    [Test]
    public async Task The_committed_machines_are_what_the_features_derive()
    {
        var derived = FeatureMachines.Derive(Glossary, await FilesAsync().ConfigureAwait(false));

        derived.Succeeded.Should().BeTrue(string.Join('\n', derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            Lf(await ReadAsync(Machines, id + ".ir.json").ConfigureAwait(false))
                .Should()
                .Be(ir + "\n", $"{id}.ir.json is stale: run trax machine from-gherkin");
        Lf(await ReadAsync(Machines, "host.json").ConfigureAwait(false))
            .Should()
            .Be(derived.Host + "\n");
        Lf(await ReadAsync(Machines, "access.txt").ConfigureAwait(false))
            .Should()
            .Be(derived.Access);
        Lf(await ReadAsync(Machines, "glossary.json").ConfigureAwait(false))
            .Should()
            .Be(Lexicon.Json, "glossary.json is stale: run trax machine from-gherkin");
    }

    [Test]
    public async Task Nothing_the_lexicon_names_is_left_out_of_the_features() =>
        FeatureMachines
            .Missing(Glossary, await FilesAsync().ConfigureAwait(false))
            .Should()
            .BeEmpty();

    // A checkout with CRLF line endings holds the same files, as trax machine from-gherkin --check reads them.
    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
