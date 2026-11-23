using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A ledger whose <c>Closed</c> state is committed and which has no effect, invoked train, role-gated event, guard
/// or reducer, so the soft save still serves it: <c>Open -Count-&gt; Counting -Close-&gt; Closed -Reopen-&gt; Open</c>. A closed
/// ledger holds at least one entry, so a stored one without any cannot be read back.
/// </summary>
public sealed class LedgerMachine : Machine<LedgerState, LedgerTrigger>
{
    public const string Id = "ledger";

    private static JsonObject Fresh() => new() { ["entries"] = new JsonArray() };

    protected override void Configure(IMachineBuilder<LedgerState, LedgerTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(LedgerState.Open, Fresh);
        m.In(LedgerState.Open)
            .Holds(ctx => ctx["entries"] is JsonArray ? null : "Open: entries[].")
            .On(LedgerTrigger.Count)
            .To(LedgerState.Counting);
        m.In(LedgerState.Counting)
            .Holds(ctx => ctx["entries"] is JsonArray ? null : "Counting: entries[].")
            .On(LedgerTrigger.Close)
            .To(LedgerState.Closed);
        m.In(LedgerState.Closed)
            .Committed()
            .Holds(ctx =>
                ctx["entries"] is JsonArray { Count: > 0 } ? null : "Closed: at least one entry."
            )
            .On(LedgerTrigger.Reopen)
            .To(LedgerState.Open);
    }

    /// <summary>A ledger in <paramref name="state"/> holding <paramref name="entries"/>, as a client sends it.</summary>
    public static string Json(LedgerState state, params int[] entries) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state.ToString(),
            ["context"] = new JsonObject
            {
                ["entries"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray()),
            },
        }.ToJsonString();
}
