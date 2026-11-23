using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// One edge an outcome can take: its target, its declarative guard and reduction (exported to the IR), and the
/// compiled forms the engine runs.
/// </summary>
internal sealed record OutcomeEdge<TState>(
    TState To,
    Rule? Guard,
    Reduction? Reduce,
    Func<JsonObject, JsonNode?, bool>? CompiledGuard,
    Func<JsonObject, JsonNode?, JsonObject>? CompiledReduce
)
    where TState : struct, Enum;
