using System.Globalization;
using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;
using Io.Cucumber.Messages.Types;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// One scenario, or one row of an outline, with its Background steps first. It is identified by file and line, never
/// by name: outlines repeat names. An outline row's line is the row's; <see cref="OutlineLine"/> is its header's.
/// </summary>
internal sealed record FeatureScenario(
    string File,
    long Line,
    string Name,
    long? OutlineLine,
    IReadOnlyList<FeatureStep> Steps,
    IReadOnlyList<string> Tags
)
{
    internal string Id => string.Create(CultureInfo.InvariantCulture, $"{File}:{Line}");

    /// <summary>
    /// The tag of a scenario that needs a handler only the host has (a custom rule's or reduction's C#, an effect's real
    /// receipt): derivation reads its steps and nothing more, an in-memory run skips it, and the host bindings run it.
    /// </summary>
    internal const string HostOnlyTag = "@host";

    /// <summary>Whether the scenario is tagged <see cref="HostOnlyTag"/>.</summary>
    internal bool HostOnly => Tags.Contains(HostOnlyTag, StringComparer.Ordinal);
}
