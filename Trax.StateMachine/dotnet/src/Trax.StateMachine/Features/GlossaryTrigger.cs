using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

internal sealed record GlossaryTrigger(
    string Name,
    string Phrase,
    JsonArray Input,
    IReadOnlyList<string> InputNames,
    JsonObject? Reduce
)
{
    /// <summary>The run-once effect a send of the trigger runs, or null.</summary>
    internal GlossaryEffect? Effect { get; init; }

    /// <summary>
    /// The guard that decides the trigger, when more than one could: its edges take it, and it must hold on every move
    /// and fail on every refusal the scenarios show.
    /// </summary>
    internal string? Guard { get; init; }
}
