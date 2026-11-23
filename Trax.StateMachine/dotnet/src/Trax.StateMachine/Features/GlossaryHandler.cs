using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// A custom rule or reduction: its name in the IR, the full name of its C# type (server-only, written to the host file),
/// and the example an in-memory run answers with: whether the rule holds, or the fields the reduction sets.
/// </summary>
internal sealed record GlossaryHandler(string Name, string Type, JsonNode Example);
