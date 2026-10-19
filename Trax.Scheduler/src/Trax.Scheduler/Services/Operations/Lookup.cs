using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>What a <see cref="LookupMatch"/> is.</summary>
public enum LookupKind
{
    /// <summary>A run (a metadata row); its id is the run's id.</summary>
    Run,

    /// <summary>A manifest; its id is the manifest's id.</summary>
    Manifest,

    /// <summary>A work queue entry; its id is the entry's id.</summary>
    WorkQueueEntry,

    /// <summary>A dead letter; its id is the dead letter's id.</summary>
    DeadLetter,

    /// <summary>
    /// A state-machine instance; its id is the instance's GUID, with
    /// <see cref="LookupMatch.Machine"/>, <see cref="LookupMatch.OwnerKind"/> and
    /// <see cref="LookupMatch.RowId"/> naming the row.
    /// </summary>
    MachineInstance,

    /// <summary>A registered train; its id is the train's full name.</summary>
    Train,
}

/// <summary>
/// One thing <see cref="IOperationsService.LookupAsync"/> found, with what a console needs to
/// open its page.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Id">
/// Its id, as text: a number for a run, manifest, entry or dead letter, a GUID for an instance,
/// the full name for a train.
/// </param>
/// <param name="Label">A short description to show: the train, manifest or machine, and its state.</param>
/// <param name="MatchedOn">
/// Which of its fields the text matched: <c>id</c>, <c>externalId</c>, <c>name</c> or <c>subjectKey</c>.
/// </param>
public sealed record LookupMatch(LookupKind Kind, string Id, string Label, string MatchedOn)
{
    /// <summary>For an instance, its machine's id; null otherwise.</summary>
    public string? Machine { get; init; }

    /// <summary>For an instance, whether a user or the system owns it; null otherwise.</summary>
    public SnapshotOwnerKind? OwnerKind { get; init; }

    /// <summary>
    /// For an instance, its row's key, which <c>machineInstance</c> takes with a user's draft;
    /// null otherwise.
    /// </summary>
    public long? RowId { get; init; }
}
