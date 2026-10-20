namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Lists every event and read each machine declares, with the rule that guards it and the surfaces that reach
/// it, so a host can review what clients and its own code may do.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/add-state-machines#census-and-instruments">SDK reference</seealso>
public static class MachineCensus
{
    /// <summary>The rule text of an event, start or read that declares none.</summary>
    public const string Undeclared = "undeclared";

    /// <summary>
    /// One entry per trigger, per read (<c>start</c>, <c>get</c>, the push (<c>&lt;machine&gt;Changed</c>)) and for <c>discard</c> of each
    /// machine. A client
    /// or effect-bound event names its rule and the surfaces that send it: on a user-owned machine its typed
    /// operation once exposed, the generic mutations while opted in, and host code acting for a client through the
    /// draft service, which is held to the same rule; on a system-owned machine, host code through
    /// <see cref="IMachineInstances"/>, as trusted code no rule is checked against. An outcome or timer trigger is
    /// listed as the server's own, which no client and no host code sends. A rule that was not declared reads
    /// <see cref="Undeclared"/>.
    /// </summary>
    /// <param name="machines">The machines to describe.</param>
    /// <exception cref="ArgumentException">A machine does not derive from <see cref="Machine{TState,TTrigger}"/>.</exception>
    public static IReadOnlyList<MachineCensusEntry> Describe(IEnumerable<IMachine> machines)
    {
        ArgumentNullException.ThrowIfNull(machines);
        var entries = new List<MachineCensusEntry>();
        foreach (var machine in machines)
        {
            if (machine is not IMachineInternals internals)
                throw new ArgumentException(
                    $"The machine '{machine.Name}' does not derive from Machine<TState, TTrigger>, so its rules "
                        + "cannot be read.",
                    nameof(machines)
                );
            entries.AddRange(Entries(machine, internals));
        }
        return entries;
    }

    private static IEnumerable<MachineCensusEntry> Entries(
        IMachine machine,
        IMachineInternals internals
    )
    {
        var rules = internals.Rules;
        var systemOwned = internals.SystemOwned;

        foreach (var trigger in machine.Triggers)
        {
            var kind = trigger.Kind.ToString();
            switch (trigger.Kind)
            {
                case TriggerKind.Outcome:
                    yield return new MachineCensusEntry(
                        machine.Name,
                        trigger.Name,
                        kind,
                        "server only: applied from the invoked train's outcome",
                        ["server (invoked train outcome)"]
                    );
                    break;
                case TriggerKind.Timer:
                    // A timer carries the context unchanged, but it acts for whoever could have put the instance in its state, so it
                    // is listed with the union of the rules of everything that enters that state.
                    var acting = internals.TimerRules.GetValueOrDefault(trigger.Name) ?? [];
                    yield return new MachineCensusEntry(
                        machine.Name,
                        trigger.Name,
                        kind,
                        acting.Count == 0
                            ? "server only: fired by the state's timer"
                            : "server only: fired by the state's timer, acting for "
                                + string.Join(" or ", acting),
                        ["server (timer)"]
                    );
                    break;
                default:
                    var rule = rules.Events.GetValueOrDefault(trigger.Name);
                    yield return new MachineCensusEntry(
                        machine.Name,
                        trigger.Name,
                        kind,
                        systemOwned && rule is null
                            ? "host code only (system-owned)"
                            : rule?.ToString() ?? Undeclared,
                        systemOwned
                            ? ["host code (IMachineInstances, trusted: no rule is checked)"]
                            : EventSurfaces(trigger.Kind)
                    );
                    break;
            }
        }

        foreach (
            var (read, rule) in new[]
            {
                ("start", rules.Start),
                ("get", rules.Get),
                ("changed", rules.Watch),
            }
        )
            yield return new MachineCensusEntry(
                machine.Name,
                read,
                "Read",
                systemOwned ? "no client reads (system-owned)" : rule?.ToString() ?? Undeclared,
                systemOwned ? [] : [$"typed {read} (when exposed)"]
            );

        yield return new MachineCensusEntry(
            machine.Name,
            "discard",
            "Discard",
            systemOwned
                ? "no client discards (system-owned)"
                : rules.Discard?.ToString() ?? Undeclared,
            systemOwned
                ? []
                :
                [
                    "typed discard (when exposed)",
                    "generic discardSnapshot (when opted in)",
                    "host code acting for a client (ISnapshotDraftService.Discard, held to the rule)",
                ]
        );
    }

    private static IReadOnlyList<string> EventSurfaces(TriggerKind kind) =>
        kind == TriggerKind.EffectBound
            ?
            [
                "typed event (when exposed)",
                "generic sendSnapshot (when opted in)",
                "host code acting for a client (ISnapshotDraftService.Send, held to the rule)",
            ]
            :
            [
                "typed event (when exposed)",
                "generic advanceSnapshot (when opted in)",
                "host code acting for a client (ISnapshotDraftService.Advance, held to the rule)",
            ];
}

/// <summary>One trigger or read of a machine, as <see cref="MachineCensus.Describe"/> lists it.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="Name">The trigger's name, the read's (<c>start</c>, <c>get</c>, the push (<c>&lt;machine&gt;Changed</c>)), or <c>discard</c>.</param>
/// <param name="Kind">The trigger's <see cref="TriggerKind"/> name, <c>Read</c> for a read, or <c>Discard</c>.</param>
/// <param name="Rule">The rule that guards it, as a reader would write it.</param>
/// <param name="Surfaces">The surfaces that reach it, such as the machine's own operations or host code.</param>
public sealed record MachineCensusEntry(
    string Machine,
    string Name,
    string Kind,
    string Rule,
    IReadOnlyList<string> Surfaces
);
