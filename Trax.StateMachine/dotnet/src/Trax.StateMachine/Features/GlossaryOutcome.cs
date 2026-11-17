using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

internal sealed record GlossaryOutcome(
    string Phrase,
    string Kind,
    JsonObject? When,
    JsonObject? Reduce
);
