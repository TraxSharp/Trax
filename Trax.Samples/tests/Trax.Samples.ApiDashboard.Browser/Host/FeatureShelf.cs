using Trax.StateMachine.Gherkin;

namespace Trax.Samples.ApiDashboard.Browser.Host;

/// <summary>
/// A machine served from features, for the console's Board page: a lexicon and one feature, derived when the host
/// starts and written to a directory the host serves with <c>ServeFeatures</c>, so its host file carries provenance.
/// </summary>
public static class FeatureShelf
{
    public const string Id = "shelf-order";

    public const string Scenario = "Ada shelves her order";

    private const string Lexicon = """
        Version: 1

        Actor: customer "a customer"
          Roles: customer

        Machine: shelf-order "order"
          Owned by: customer
          Started by: "has an order"
          State: Open    "is open"
          State: Shelved "is shelved"
          Trigger: Shelve "shelves {subject}"
        """;

    private const string Feature = $"""
        Feature: Shelving
          Scenario: {Scenario}
            Given Ada is a customer
            And Ada has an order
            When Ada shelves her order
            Then Ada's order is shelved
        """;

    /// <summary>Derives the machine and writes its directory; returns the directory.</summary>
    public static string Write()
    {
        var glossary = Trax
            .StateMachine.Gherkin.Lexicon.Read("shelving.lexicon", Lexicon)
            .GlossaryOrThrow();
        var derived = FeatureMachines.Derive(
            glossary,
            [new FeatureFile("shelving.feature", Feature)]
        );
        if (!derived.Succeeded)
            throw new InvalidOperationException(string.Join("\n", derived.Problems));
        var directory = Path.Combine(Path.GetTempPath(), $"trax-console-shelf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        foreach (var (id, ir) in derived.Irs)
            File.WriteAllText(Path.Combine(directory, id + ".ir.json"), ir);
        File.WriteAllText(Path.Combine(directory, "host.json"), derived.Host);
        return directory;
    }
}
