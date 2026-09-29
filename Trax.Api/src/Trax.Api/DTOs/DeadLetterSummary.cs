using Trax.Effect.Enums;

namespace Trax.Api.DTOs;

/// <summary>
/// A dead letter: a manifest run that exhausted its retries and now waits for an operator to
/// retry or acknowledge it. The scheduler never retries a dead letter on its own.
/// </summary>
/// <param name="Id">The dead letter's id, used by the retry and acknowledge mutations.</param>
/// <param name="ManifestId">The id of the manifest whose run was dead-lettered.</param>
/// <param name="ManifestName">The name of the train that manifest runs, or <c>Unknown</c> when the manifest can no longer be read.</param>
/// <param name="Status">Whether the dead letter still awaits intervention, was retried, or was acknowledged.</param>
/// <param name="DeadLetteredAt">When the run was dead-lettered (UTC).</param>
/// <param name="Reason">Why the run was dead-lettered.</param>
/// <param name="RetryCountAtDeadLetter">How many retries had been made when the run was dead-lettered.</param>
/// <param name="ResolvedAt">When the dead letter was retried or acknowledged (UTC), or <c>null</c> while it awaits intervention.</param>
/// <param name="ResolutionNote">The note the operator left when resolving it, if any.</param>
/// <param name="RetryMetadataId">The id of the execution a retry started, once the dead letter has been retried.</param>
public record DeadLetterSummary(
    long Id,
    long ManifestId,
    string ManifestName,
    DeadLetterStatus Status,
    DateTime DeadLetteredAt,
    string Reason,
    int RetryCountAtDeadLetter,
    DateTime? ResolvedAt,
    string? ResolutionNote,
    long? RetryMetadataId
);
