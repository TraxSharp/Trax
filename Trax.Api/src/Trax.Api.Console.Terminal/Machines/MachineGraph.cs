using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;

namespace Trax.Api.Console.Terminal.Machines;

/// <summary>
/// A machine's graph as text, from what <c>Machines</c> answers: each state, marked initial, final or current, with
/// what takes it where (a command, an outcome of the train it invokes, a timer).
/// </summary>
internal static class MachineGraph
{
    public static IReadOnlyList<string> Lines(
        MachinesResultOperationsMachines machine,
        string? current = null
    )
    {
        if (machine.GraphUnavailableReason is { } reason)
            return [$"No graph: {reason}"];
        var lines = new List<string>();
        foreach (var state in machine.States)
        {
            var mark =
                state.Name == current ? "▶"
                : state.IsInitial ? "●"
                : state.IsFinal ? "■"
                : "○";
            var notes = new[]
            {
                state.IsInitial ? "initial" : null,
                state.IsFinal ? "final" : null,
                state.IsCommitted ? "committed" : null,
                state.InvokesTrain is { } train ? $"invokes {train}" : null,
                state.SpawnsMachine is { } child ? $"spawns {child}" : null,
                state.Name == current ? "current" : null,
            }.Where(n => n is not null);
            var name = state.Name == current ? $"[{state.Name}]" : state.Name;
            lines.Add($"{mark} {name}{(notes.Any() ? $"  ({string.Join(", ", notes)})" : "")}");
            foreach (var t in machine.Transitions.Where(t => t.From == state.Name))
            {
                var how = t.After is { } after ? $"after {after}" : t.Command;
                var extra = new[]
                {
                    Wire.Name(t.Kind).ToLowerInvariant(),
                    t.Guard is { } guard ? $"if {guard}" : null,
                    t.Effect is { } effect ? $"then {effect}" : null,
                }.Where(e => e is not null);
                lines.Add($"    {how} ──▶ {t.To}  ({string.Join(", ", extra)})");
            }
            foreach (var timer in machine.Timers.Where(t => t.State == state.Name))
                lines.Add(
                    $"    timer {timer.Command} after {timer.After ?? timer.Field ?? "?"} ──▶ {timer.Target}"
                );
        }
        return lines;
    }
}
