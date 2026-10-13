using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

public enum MemoState
{
    Open,
    Filed,
}

public enum MemoTrigger
{
    File,
    Reopen,
}

public sealed record MemoContext
{
    public string? Text { get; init; }
}

public sealed record MemoStartInput
{
    public string Text { get; init; } = "";
}

/// <summary>
/// A second declarative machine with every rule declared. It is registered wherever <see cref="WizardMachine"/> is
/// and, unless a host serves it (<see cref="E2EHostOptions.Served"/>), must get no typed operations of its own; a test
/// that needs two served machines in one operation names it.
/// </summary>
public class MemoMachine : Machine<MemoState, MemoTrigger>
{
    public const string MachineId = "memo";

    protected override void Configure(IMachineBuilder<MemoState, MemoTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(MemoState.Open, () => new JsonObject());
        m.Start<MemoStartInput>(s =>
            s.Reduce(Set((MemoContext c) => c.Text).FromInput((MemoStartInput i) => i.Text))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(MemoState.Open)
            .Context<MemoContext>()
            .On(MemoTrigger.File)
            .AnyAuthenticated()
            .To(MemoState.Filed);
        m.In(MemoState.Filed)
            .Context<MemoContext>()
            .On(MemoTrigger.Reopen)
            .AnyAuthenticated()
            .To(MemoState.Open);
    }
}
