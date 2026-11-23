using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// One state of a glossary machine: <see cref="Phrase"/>, whether it is <see cref="Committed"/>, the rule its context
/// <see cref="Requires"/>, and its own <see cref="Context"/>, null when it has the machine's.
/// </summary>
internal sealed record GlossaryState(
    string Name,
    string Phrase,
    bool Committed,
    JsonObject? Requires,
    JsonArray? Context,
    IReadOnlyList<string>? ContextNames
);
