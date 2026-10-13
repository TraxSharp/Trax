using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

public enum SnoozeState
{
    Waiting,
    Awake,
    Lapsed,
}

public enum SnoozeTrigger
{
    Wake,
    Snooze,
}

public sealed record SnoozeContext
{
    /// <summary>How long <see cref="SnoozeState.Waiting"/> is held before it lapses.</summary>
    public TimeSpan? Patience { get; init; }
}

/// <summary>What a client starts a snooze with, and snoozes again with.</summary>
public sealed record SnoozeInput
{
    public TimeSpan Patience { get; init; }
}

/// <summary>
/// A machine whose timer reads its duration from the context: <c>start</c> and <c>Snooze</c> carry a
/// <see cref="TimeSpan"/> the reduction puts in <c>patience</c>, and <see cref="SnoozeState.Waiting"/> lapses once it
/// has been held that long, bounded by a minute and an hour.
/// <code>
/// start --> Waiting --Wake--> Awake --Snooze--> Waiting
///              │ after patience
///              └──────> Lapsed --Snooze--> Waiting
/// </code>
/// </summary>
public class SnoozeMachine : Machine<SnoozeState, SnoozeTrigger>
{
    public const string MachineId = "snooze";

    public static readonly TimeSpan Min = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Max = TimeSpan.FromHours(1);

    protected override void Configure(IMachineBuilder<SnoozeState, SnoozeTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(SnoozeState.Waiting, () => new JsonObject());
        m.Start<SnoozeInput>(s => s.Reduce(Patience).AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(SnoozeState.Waiting)
            .Context<SnoozeContext>()
            .After<SnoozeContext>(c => c.Patience, Min, Max)
            .GoTo(SnoozeState.Lapsed)
            .On(SnoozeTrigger.Wake)
            .AnyAuthenticated()
            .To(SnoozeState.Awake);

        foreach (var resting in new[] { SnoozeState.Awake, SnoozeState.Lapsed })
            m.In(resting)
                .Context<SnoozeContext>()
                .On(SnoozeTrigger.Snooze)
                .WithInput<SnoozeInput>()
                .AnyAuthenticated()
                .Reduce(Patience)
                .To(SnoozeState.Waiting);
    }

    private static Reduction Patience =>
        Set((SnoozeContext c) => c.Patience).FromInput((SnoozeInput i) => i.Patience);
}
