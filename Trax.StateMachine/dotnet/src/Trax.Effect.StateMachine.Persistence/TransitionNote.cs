using Trax.Effect.Enums;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// What the store records in an instance's history for one snapshot write: what wrote it, the command that moved it,
/// and who made it. The states, the revision and the time come from the write itself. See
/// <c>Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </summary>
/// <param name="Kind">What wrote it.</param>
/// <param name="Trigger">The command, or null when none moved it.</param>
/// <param name="Actor">Who made it.</param>
internal readonly record struct TransitionNote(
    SnapshotTransitionKind Kind,
    string? Trigger,
    SnapshotTransitionActor Actor
)
{
    /// <summary>The actor of a write <paramref name="owner"/>'s side makes: the owner for a user's row, host code for a system one.</summary>
    public static SnapshotTransitionActor ActorFor(DraftOwner owner) =>
        owner.Kind == SnapshotOwnerKind.User
            ? SnapshotTransitionActor.Owner
            : SnapshotTransitionActor.Host;

    /// <summary>A creation by <paramref name="owner"/>'s side.</summary>
    public static TransitionNote Start(DraftOwner owner) =>
        new(SnapshotTransitionKind.Start, null, ActorFor(owner));

    /// <summary>A command <paramref name="trigger"/> sent by <paramref name="owner"/>'s side.</summary>
    public static TransitionNote Command(DraftOwner owner, string? trigger) =>
        new(SnapshotTransitionKind.Command, trigger, ActorFor(owner));

    /// <summary>A save by <paramref name="owner"/>'s side.</summary>
    public static TransitionNote Save(DraftOwner owner) =>
        new(SnapshotTransitionKind.Save, null, ActorFor(owner));

    /// <summary>A timer's fire of <paramref name="trigger"/>.</summary>
    public static TransitionNote Timer(string? trigger) =>
        new(SnapshotTransitionKind.Timer, trigger, SnapshotTransitionActor.Machine);

    /// <summary>A run's outcome, applied as <paramref name="trigger"/>, or null when it could not be.</summary>
    public static TransitionNote Outcome(string? trigger) =>
        new(SnapshotTransitionKind.Outcome, trigger, SnapshotTransitionActor.Machine);

    /// <summary>A child's creation by the parent that spawned it.</summary>
    public static TransitionNote Spawned { get; } =
        new(SnapshotTransitionKind.Start, null, SnapshotTransitionActor.Machine);
}
