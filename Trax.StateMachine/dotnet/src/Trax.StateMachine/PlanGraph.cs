using System.Text.Json.Nodes;

namespace Trax.StateMachine;

// What the planner needs of an engine: its states, which triggers a client may move with, which edges it can evaluate,
// and its advance and wire. The search itself is the same for every engine, so it answers the same plan.
internal sealed record PlanGraph(
    int StateCount,
    Func<string, bool> IsState,
    Func<string, bool> Navigable,
    Func<string, string, bool> Determines,
    Func<Snapshot, string, JsonNode?, AdvanceResult> Advance,
    Func<Snapshot, string> Serialize
);
