using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

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
