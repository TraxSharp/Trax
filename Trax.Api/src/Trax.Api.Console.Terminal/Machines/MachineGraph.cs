using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Trax.Api.Console.Terminal.Operations;

namespace Trax.Api.Console.Terminal.Machines;

/// <summary>
/// A machine's graph as boxes and arrows (<see cref="GraphLayout"/>), from what <c>Machines</c> answers: a transition
/// taken by a command is labelled with it, one taken by the outcome of the train its state invokes with
/// <c>on</c> and the outcome, and one a timer takes with <c>after</c> and its delay. What a state invokes or spawns is
/// in the legend under the graph. A machine's drawing is kept for each current state and direction it was drawn with,
/// for as long as the machine's record is, so a window read again on every change does not lay it out again.
/// </summary>
internal static class MachineGraph
{
    private static readonly ConditionalWeakTable<
        MachinesResultOperationsMachines,
        ConcurrentDictionary<(string? Current, bool Vertical), IReadOnlyList<string>>
    > Drawn = new();

    public static IReadOnlyList<string> Lines(
        MachinesResultOperationsMachines machine,
        string? current = null,
        bool vertical = false
    ) =>
        Drawn
            .GetValue(machine, _ => new())
            .GetOrAdd((current, vertical), key => Draw(machine, key.Current, key.Vertical));

    private static IReadOnlyList<string> Draw(
        MachinesResultOperationsMachines machine,
        string? current,
        bool vertical
    )
    {
        if (machine.GraphUnavailableReason is { } reason)
            return [$"No graph: {reason}"];
        var states = machine
            .States.Select(s => new GraphState(
                s.Name,
                s.IsInitial || s.Name == machine.InitialState,
                s.IsFinal,
                Note(s)
            ))
            .ToList();
        var edges = machine
            .Transitions.Select(t => new GraphEdge(
                t.From,
                t.To,
                t.Kind switch
                {
                    MachineTransitionKind.Timer => $"after {Delay(t.After) ?? t.Command}",
                    // An outcome is named after the state that invokes the train (Ordered.done): the line leaves
                    // that state already.
                    MachineTransitionKind.Outcome =>
                        $"on {(t.Command.StartsWith(t.From + ".", StringComparison.Ordinal) ? t.Command[(t.From.Length + 1)..] : t.Command)}",
                    _ => t.Command,
                }
            ))
            .ToList();
        foreach (var timer in machine.Timers)
            if (
                !machine.Transitions.Any(t =>
                    t.From == timer.State && t.To == timer.Target && t.Command == timer.Command
                )
            )
                edges.Add(
                    new GraphEdge(
                        timer.State,
                        timer.Target,
                        $"after {Delay(timer.After) ?? timer.Field ?? timer.Command}"
                    )
                );
        return GraphLayout.Render(states, edges, current, vertical);
    }

    // An ISO 8601 duration as the console writes a delay: PT30M is 30m, P1DT2H is 1d2h.
    private static string? Delay(string? iso)
    {
        if (iso is null)
            return null;
        TimeSpan span;
        try
        {
            span = System.Xml.XmlConvert.ToTimeSpan(iso);
        }
        catch (FormatException)
        {
            return iso;
        }
        var parts = new[]
        {
            span.Days > 0 ? $"{span.Days}d" : null,
            span.Hours > 0 ? $"{span.Hours}h" : null,
            span.Minutes > 0 ? $"{span.Minutes}m" : null,
            span.Seconds > 0 ? $"{span.Seconds}s" : null,
        }.Where(p => p is not null);
        var text = string.Concat(parts);
        return text.Length == 0 ? "0s" : text;
    }

    private static string? Note(MachinesResultOperationsMachinesStates state)
    {
        var notes = new[]
        {
            state.IsCommitted ? "committed" : null,
            state.InvokesTrain is { } train ? $"invokes {train}" : null,
            state.SpawnsMachine is { } child ? $"spawns {child}" : null,
        }
            .Where(n => n is not null)
            .ToList();
        return notes.Count == 0 ? null : string.Join(", ", notes);
    }
}
