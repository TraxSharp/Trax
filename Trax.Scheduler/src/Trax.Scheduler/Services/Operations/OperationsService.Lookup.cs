using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

// Finding a thing by the id an operator holds (central docs/0022): one read per kind and field,
// each an exact match on an indexed column, so a lookup costs a handful of index probes whatever
// the size of the tables.
public partial class OperationsService
{
    /// <summary>The most matches <see cref="LookupAsync"/> returns of each kind.</summary>
    public const int LookupMatchCap = 10;

    /// <summary>
    /// The longest text <see cref="LookupAsync"/> looks up. Every id, key and name it matches is
    /// shorter; a longer text finds nothing and reads nothing.
    /// </summary>
    public const int LookupTextLimit = 512;

    /// <inheritdoc />
    /// <remarks>
    /// <para>The reads, by what the text parses as: a positive whole number is looked up as the
    /// primary key of a run, manifest, work queue entry and dead letter; a GUID through
    /// <c>ix_snapshot_draft_id</c> (Trax.Effect's Postgres migration 086); any text as a run's
    /// external id (<c>ix_metadata_external_id</c>), a manifest's external id and name
    /// (<c>ix_manifest_external_id</c>, <c>manifest_name_idx</c>), an entry's external id
    /// (<c>ix_work_queue_external_id</c>) and subject key (<c>ix_work_queue_subject_queued</c> and
    /// <c>ix_work_queue_subject_busy</c>, which hold the queued and dispatched entries), and a
    /// registered train's name, from the host's discovery.</para>
    /// <para>Nothing is matched by prefix or contents. Matches come in that order of kind, newest
    /// first within a kind.</para>
    /// </remarks>
    public async Task<IReadOnlyList<LookupMatch>> LookupAsync(string text, CancellationToken ct)
    {
        var term = text?.Trim() ?? "";
        if (term.Length == 0 || term.Length > LookupTextLimit)
            return [];

        var matches = new List<LookupMatch>();
        long? number = long.TryParse(term, out var n) && n > 0 ? n : null;
        Guid? guid = Guid.TryParse(term, out var g) ? g : null;

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        // Runs.
        if (number is { } runId)
            matches.AddRange(
                (
                    await db
                        .Metadatas.AsNoTracking()
                        .Where(m => m.Id == runId)
                        .Select(m => new
                        {
                            m.Id,
                            m.Name,
                            m.TrainState,
                        })
                        .ToListAsync(ct)
                ).Select(m => RunMatch(m.Id, m.Name, m.TrainState, "id"))
            );
        matches.AddRange(
            (
                await db
                    .Metadatas.AsNoTracking()
                    .Where(m => m.ExternalId == term)
                    .OrderByDescending(m => m.Id)
                    .Take(LookupMatchCap)
                    .Select(m => new
                    {
                        m.Id,
                        m.Name,
                        m.TrainState,
                    })
                    .ToListAsync(ct)
            ).Select(m => RunMatch(m.Id, m.Name, m.TrainState, "externalId"))
        );

        // Manifests.
        if (number is { } manifestId)
            matches.AddRange(
                (
                    await db
                        .Manifests.AsNoTracking()
                        .Where(m => m.Id == manifestId)
                        .Select(m => new { m.Id, m.Name })
                        .ToListAsync(ct)
                ).Select(m => ManifestMatch(m.Id, m.Name, "id"))
            );
        matches.AddRange(
            (
                await db
                    .Manifests.AsNoTracking()
                    .Where(m => m.ExternalId == term)
                    .Take(LookupMatchCap)
                    .Select(m => new { m.Id, m.Name })
                    .ToListAsync(ct)
            ).Select(m => ManifestMatch(m.Id, m.Name, "externalId"))
        );
        matches.AddRange(
            (
                await db
                    .Manifests.AsNoTracking()
                    .Where(m => m.Name == term)
                    .OrderByDescending(m => m.Id)
                    .Take(LookupMatchCap)
                    .Select(m => new { m.Id, m.Name })
                    .ToListAsync(ct)
            ).Select(m => ManifestMatch(m.Id, m.Name, "name"))
        );

        // Work queue entries.
        if (number is { } entryId)
            matches.AddRange(
                (
                    await db
                        .WorkQueues.AsNoTracking()
                        .Where(w => w.Id == entryId)
                        .Select(w => new
                        {
                            w.Id,
                            w.TrainName,
                            w.Status,
                        })
                        .ToListAsync(ct)
                ).Select(w => EntryMatch(w.Id, w.TrainName, w.Status, "id"))
            );
        matches.AddRange(
            (
                await db
                    .WorkQueues.AsNoTracking()
                    .Where(w => w.ExternalId == term)
                    .Take(LookupMatchCap)
                    .Select(w => new
                    {
                        w.Id,
                        w.TrainName,
                        w.Status,
                    })
                    .ToListAsync(ct)
            ).Select(w => EntryMatch(w.Id, w.TrainName, w.Status, "externalId"))
        );
        // Each status on its own, so each read is the partial index that holds it.
        foreach (var status in new[] { WorkQueueStatus.Queued, WorkQueueStatus.Dispatched })
        {
            var room = LookupMatchCap - matches.Count(x => x.MatchedOn == "subjectKey");
            if (room <= 0)
                break;
            matches.AddRange(
                (
                    await db
                        .WorkQueues.AsNoTracking()
                        .Where(w => w.SubjectKey == term && w.Status == status)
                        .OrderByDescending(w => w.Id)
                        .Take(room)
                        .Select(w => new
                        {
                            w.Id,
                            w.TrainName,
                            w.Status,
                        })
                        .ToListAsync(ct)
                ).Select(w => EntryMatch(w.Id, w.TrainName, w.Status, "subjectKey"))
            );
        }

        // Dead letters.
        if (number is { } deadLetterId)
            matches.AddRange(
                (
                    await db
                        .DeadLetters.AsNoTracking()
                        .Where(d => d.Id == deadLetterId)
                        .Select(d => new
                        {
                            d.Id,
                            Name = d.Manifest!.Name,
                            d.Status,
                        })
                        .ToListAsync(ct)
                ).Select(d => new LookupMatch(
                    LookupKind.DeadLetter,
                    d.Id.ToString(),
                    $"{d.Name} · {d.Status}",
                    "id"
                ))
            );

        // State-machine instances: what the operator's list shows of a row, never its context or
        // its owner's key.
        if (guid is { } instanceId)
            matches.AddRange(
                (
                    await db
                        .SnapshotDrafts.AsNoTracking()
                        .Where(x => x.Id == instanceId)
                        .OrderByDescending(x => x.RowId)
                        .Take(LookupMatchCap)
                        .Select(x => new
                        {
                            x.Id,
                            x.Machine,
                            x.State,
                            x.OwnerKind,
                            x.RowId,
                        })
                        .ToListAsync(ct)
                ).Select(x => new LookupMatch(
                    LookupKind.MachineInstance,
                    x.Id.ToString(),
                    $"{x.Machine} · {x.State}",
                    "id"
                )
                {
                    Machine = x.Machine,
                    OwnerKind = x.OwnerKind,
                    RowId = x.RowId,
                })
            );

        // Trains, from the host's registry.
        matches.AddRange(
            _discoveryService
                .DiscoverTrains()
                .Where(r =>
                    string.Equals(r.ServiceType.FullName, term, StringComparison.Ordinal)
                    || string.Equals(r.ServiceTypeName, term, StringComparison.Ordinal)
                )
                .Take(LookupMatchCap)
                .Select(r => new LookupMatch(
                    LookupKind.Train,
                    r.ServiceType.FullName!,
                    r.ServiceTypeName,
                    "name"
                ))
        );

        return matches;
    }

    private static LookupMatch RunMatch(long id, string train, TrainState state, string on) =>
        new(LookupKind.Run, id.ToString(), $"{train} · {state}", on);

    private static LookupMatch ManifestMatch(long id, string name, string on) =>
        new(LookupKind.Manifest, id.ToString(), name, on);

    private static LookupMatch EntryMatch(
        long id,
        string train,
        WorkQueueStatus status,
        string on
    ) => new(LookupKind.WorkQueueEntry, id.ToString(), $"{train} · {status}", on);
}
