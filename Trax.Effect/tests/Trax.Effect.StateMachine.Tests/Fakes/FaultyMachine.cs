using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Tests.Fakes;

public enum FaultyState
{
    A,
    B,
}

public enum FaultyTrigger
{
    Boom,
    Bad,
    Trap,
}

/// <summary>
/// A machine whose handlers misbehave, to prove the engine failure modes that must stay total: a reducer
/// that THROWS degrades to <c>internal-error</c> (the totality backstop), a reducer that produces a
/// context the target state rejects degrades to <c>invalid-context</c> (a reducer bug, not user error),
/// and a GUARD that throws also degrades to <c>internal-error</c> (guards are evaluated inside the
/// backstop too — PD4). None ever surfaces an exception.
/// </summary>
public static class FaultyMachine
{
    public static readonly SnapshotMachine<FaultyState, FaultyTrigger> Machine = new(
        new MachineDefinition<FaultyState, FaultyTrigger>
        {
            Id = "faulty",
            Version = 1,
            InitialState = FaultyState.A,
            CreateInitialContext = () => new JsonObject(),
            Transitions = new[]
            {
                new TransitionDefinition<FaultyState, FaultyTrigger>
                {
                    From = FaultyState.A,
                    Trigger = FaultyTrigger.Boom,
                    To = FaultyState.B,
                    Reduce = (_, _) => throw new InvalidOperationException("reducer blew up"),
                },
                new TransitionDefinition<FaultyState, FaultyTrigger>
                {
                    From = FaultyState.A,
                    Trigger = FaultyTrigger.Bad,
                    To = FaultyState.B,
                    // Produces an empty context, but B requires "ok" — so the target validator rejects it.
                    Reduce = (_, _) => new JsonObject(),
                },
                new TransitionDefinition<FaultyState, FaultyTrigger>
                {
                    From = FaultyState.A,
                    Trigger = FaultyTrigger.Trap,
                    To = FaultyState.B,
                    // A guard that throws must be caught by the totality backstop, not escape Advance.
                    Guard = (_, _) => throw new InvalidOperationException("guard blew up"),
                },
            },
            ContextValidators = new Dictionary<FaultyState, Func<JsonObject, string?>>
            {
                [FaultyState.B] = ctx =>
                    ctx["ok"]?.GetValueKind() == JsonValueKind.True ? null : "B requires ok=true.",
            },
        }
    );
}
