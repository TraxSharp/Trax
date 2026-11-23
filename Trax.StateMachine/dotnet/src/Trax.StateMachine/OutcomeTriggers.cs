using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>
/// Names outcome triggers. A trigger enum member cannot contain a dot, so <c>&lt;State&gt;.done</c> never collides
/// with a user's trigger, and the TypeScript twin builds the same names from the IR.
/// </summary>
internal static class OutcomeTriggers
{
    public static string Name<TState>(TState state, InvokeOutcomeKind kind)
        where TState : struct, Enum => $"{state}.{Suffix(kind)}";

    public static string Suffix(InvokeOutcomeKind kind) =>
        kind switch
        {
            InvokeOutcomeKind.Done => "done",
            InvokeOutcomeKind.Failed => "failed",
            _ => "cancelled",
        };

    /// <summary>Splits <c>&lt;State&gt;.&lt;kind&gt;</c>; false for anything else.</summary>
    public static bool TryParse(string trigger, out string state, out InvokeOutcomeKind kind)
    {
        state = "";
        kind = default;
        var dot = trigger.LastIndexOf('.');
        if (dot <= 0)
            return false;
        state = trigger[..dot];
        switch (trigger[(dot + 1)..])
        {
            case "done":
                kind = InvokeOutcomeKind.Done;
                return true;
            case "failed":
                kind = InvokeOutcomeKind.Failed;
                return true;
            case "cancelled":
                kind = InvokeOutcomeKind.Cancelled;
                return true;
            default:
                return false;
        }
    }
}
