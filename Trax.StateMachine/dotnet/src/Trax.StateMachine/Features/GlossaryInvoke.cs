using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

internal sealed record GlossaryInvoke(
    string State,
    string Train,
    IReadOnlyDictionary<string, string> Input,
    JsonArray Output,
    IReadOnlyList<string> OutputNames,
    IReadOnlyDictionary<string, GlossaryOutcome> Outcomes,
    IReadOnlyDictionary<string, JsonObject> Examples
);
