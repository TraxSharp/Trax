using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

public enum TicketState
{
    Open,
    Triaged,
}

public enum TicketTrigger
{
    Triage,
}

/// <summary>A priority whose member names are not upper snake case, so a spelling that differs from them shows.</summary>
public enum TicketPriority
{
    Low,
    HighPriority,
}

public sealed record TicketContext
{
    public string? Title { get; init; }

    public TicketPriority? Priority { get; init; }
}

public sealed record TicketStartInput
{
    public string Title { get; init; } = "";
}

public sealed record TicketTriageInput
{
    public TicketPriority Priority { get; init; }
}

/// <summary>A machine with an enum in its context and in an event's input, for how an enum is spelled on the wire.</summary>
public class TicketMachine : Machine<TicketState, TicketTrigger>
{
    public const string MachineId = "ticket";

    protected override void Configure(IMachineBuilder<TicketState, TicketTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(TicketState.Open, () => new JsonObject());
        m.Start<TicketStartInput>(s =>
            s.Reduce(Set((TicketContext c) => c.Title).FromInput((TicketStartInput i) => i.Title))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(TicketState.Open)
            .Context<TicketContext>()
            .On(TicketTrigger.Triage)
            .WithInput<TicketTriageInput>()
            .AnyAuthenticated()
            .Reduce(
                Set((TicketContext c) => c.Priority).FromInput((TicketTriageInput i) => i.Priority)
            )
            .To(TicketState.Triaged);
        m.In(TicketState.Triaged).Context<TicketContext>();
    }
}
