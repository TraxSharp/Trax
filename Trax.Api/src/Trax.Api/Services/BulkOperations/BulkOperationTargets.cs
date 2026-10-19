using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.Services.Lists;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Services.BulkOperations;

/// <summary>
/// What each kind of bulk operation reads and does: the snapshot of the ids its filter matches,
/// and one batch of them through the shared operation the id-list mutation of that kind calls
/// (central <c>docs/0022</c>). Nothing here writes a row itself.
/// </summary>
/// <remarks>
/// A batch is judged by reading its ids back once the shared operation has run: an id is done when
/// it is in the state the operation asks for, and failed otherwise, with the reason its row gives.
/// The shared operations report counts, not ids, and the read-back also catches a row that moved
/// between the snapshot and its batch.
/// </remarks>
internal static class BulkOperationTargets
{
    /// <summary>How many ids one snapshot read returns, in id order.</summary>
    public const int SnapshotPageSize = 10_000;

    /// <summary>The reason for an id whose row is gone by the time its batch runs.</summary>
    public const string GoneReason = "It no longer exists.";

    /// <summary>The list a kind applies to, or <c>null</c> when the filter names the wrong one.</summary>
    public static string? Refusal(BulkOperationKind kind, BulkOperationFilter? filter)
    {
        var given =
            (filter?.Executions is null ? 0 : 1)
            + (filter?.DeadLetters is null ? 0 : 1)
            + (filter?.Manifests is null ? 0 : 1);
        if (given != 1)
            return "Give exactly one of filter.executions, filter.deadLetters or filter.manifests.";

        var fits = kind switch
        {
            BulkOperationKind.Cancel => filter!.Executions is not null,
            BulkOperationKind.Requeue or BulkOperationKind.Acknowledge => filter!.DeadLetters
                is not null,
            _ => filter!.Manifests is not null,
        };
        return fits
            ? null
            : kind switch
            {
                BulkOperationKind.Cancel => "CANCEL applies to runs: give filter.executions.",
                BulkOperationKind.Requeue or BulkOperationKind.Acknowledge =>
                    $"{kind.ToString().ToUpperInvariant()} applies to dead letters: give filter.deadLetters.",
                _ =>
                    $"{kind.ToString().ToUpperInvariant()} applies to manifests: give filter.manifests.",
            };
    }

    /// <summary>
    /// Every id the filter matches now, read a page at a time so no one read holds the table for
    /// long, and the batches to run them in: ascending ids, <see cref="BulkOperationJobs.BatchSize"/>
    /// at a time. Dead letters to requeue are the exception (see <see cref="RequeueBatches"/>).
    /// </summary>
    public static async Task<BulkOperationSnapshot> SnapshotAsync(
        IDataContextProviderFactory factory,
        BulkOperationKind kind,
        BulkOperationFilter filter,
        CancellationToken ct
    )
    {
        if (kind == BulkOperationKind.Requeue)
            return await RequeueSnapshotAsync(factory, filter.DeadLetters!, ct);

        var ids = new List<long>();
        var after = long.MinValue;
        while (true)
        {
            using var db = await factory.CreateDbContextAsync(ct);
            IQueryable<long> matching = filter switch
            {
                { Executions: { } runs } => ListFilters
                    .Executions(db, db.Metadatas.AsNoTracking(), runs)
                    .Select(m => m.Id),
                { DeadLetters: { } deadLetters } => ListFilters
                    .DeadLetters(db.DeadLetters.AsNoTracking(), deadLetters)
                    .Select(d => d.Id),
                { Manifests: { } manifests } => ListFilters
                    .Manifests(db.Manifests.AsNoTracking(), manifests)
                    .Select(m => m.Id),
                _ => throw new ArgumentException("No list given.", nameof(filter)),
            };
            var cursor = after;
            var page = await matching
                .Where(id => id > cursor)
                .OrderBy(id => id)
                .Take(SnapshotPageSize)
                .ToListAsync(ct);
            ids.AddRange(page);
            if (page.Count < SnapshotPageSize)
                break;
            after = page[^1];
        }

        var ends = new List<int>();
        for (
            var end = BulkOperationJobs.BatchSize;
            end < ids.Count;
            end += BulkOperationJobs.BatchSize
        )
            ends.Add(end);
        if (ids.Count > 0)
            ends.Add(ids.Count);
        return new BulkOperationSnapshot([.. ids], [.. ends]);
    }

    /// <summary>
    /// Dead letters to requeue, grouped by manifest. The shared requeue folds the dead letters of
    /// one manifest in one batch into a single queued run and resolves them all, and skips a dead
    /// letter whose manifest already has a queued run; split across two batches, a manifest's
    /// second half would be skipped. So a batch holds whole manifests, up to
    /// <see cref="BulkOperationJobs.BatchSize"/> dead letters, and a manifest with more than that
    /// gets a batch of its own of up to <see cref="OperationsService.MaxBatchSize"/>, the most the
    /// shared requeue takes. Past that its dead letters are left awaiting, each with the reason.
    /// </summary>
    private static async Task<BulkOperationSnapshot> RequeueSnapshotAsync(
        IDataContextProviderFactory factory,
        BulkDeadLetterFilter filter,
        CancellationToken ct
    )
    {
        var rows = new List<(long ManifestId, long Id)>();
        (long ManifestId, long Id) after = (long.MinValue, long.MinValue);
        while (true)
        {
            using var db = await factory.CreateDbContextAsync(ct);
            var (manifest, id) = after;
            var page = await ListFilters
                .DeadLetters(db.DeadLetters.AsNoTracking(), filter)
                .Where(d => d.ManifestId > manifest || (d.ManifestId == manifest && d.Id > id))
                .OrderBy(d => d.ManifestId)
                .ThenBy(d => d.Id)
                .Take(SnapshotPageSize)
                .Select(d => new { d.ManifestId, d.Id })
                .ToListAsync(ct);
            rows.AddRange(page.Select(r => (r.ManifestId, r.Id)));
            if (page.Count < SnapshotPageSize)
                break;
            after = (page[^1].ManifestId, page[^1].Id);
        }

        return new BulkOperationSnapshot(
            rows.Select(r => r.Id).ToArray(),
            RequeueBatches(rows.Select(r => r.ManifestId).ToList())
        );
    }

    /// <summary>
    /// Where each requeue batch ends, given the manifest of each dead letter in snapshot order.
    /// </summary>
    internal static int[] RequeueBatches(IReadOnlyList<long> manifests)
    {
        var ends = new List<int>();
        var start = 0;
        var i = 0;
        while (i < manifests.Count)
        {
            // The run of dead letters sharing this manifest.
            var runEnd = i;
            while (runEnd < manifests.Count && manifests[runEnd] == manifests[i])
                runEnd++;
            var run = runEnd - i;

            if (run > BulkOperationJobs.BatchSize)
            {
                if (i > start)
                    ends.Add(i);
                for (
                    var end = i + OperationsService.MaxBatchSize;
                    end < runEnd;
                    end += OperationsService.MaxBatchSize
                )
                    ends.Add(end);
                ends.Add(runEnd);
                start = runEnd;
            }
            else if (runEnd - start > BulkOperationJobs.BatchSize)
            {
                ends.Add(i);
                start = i;
            }
            i = runEnd;
        }
        if (manifests.Count > start)
            ends.Add(manifests.Count);
        return [.. ends];
    }

    /// <summary>
    /// Runs one batch. Returns the ids that did not take, each with its reason, or a refusal that
    /// stops the operation (the host cannot do this kind of operation at all).
    /// </summary>
    public static async Task<(
        IReadOnlyList<BulkOperationFailure> Failures,
        string? Refusal
    )> RunAsync(BulkOperationRun run, long[] batch, IServiceProvider services, CancellationToken ct)
    {
        var factory = services.GetRequiredService<IDataContextProviderFactory>();
        switch (run.Kind)
        {
            case BulkOperationKind.Cancel:
            {
                // A batch is never empty nor over the batch limit, so the service refuses nothing
                // as a whole; a run a user's draft started is skipped and reported per id below.
                await services
                    .GetRequiredService<IOperationsService>()
                    .CancelExecutionsAsync(batch, ct);
                return (await CancelFailuresAsync(factory, batch, ct), null);
            }
            case BulkOperationKind.Requeue:
            {
                var scheduler = services.GetRequiredService<ITraxScheduler>();
                // The overload is called only when asked, so a host whose scheduler predates it
                // keeps the default path.
                var result = run.AskAfresh
                    ? await scheduler.RequeueDeadLettersAsync(batch, askAfresh: true, ct)
                    : await scheduler.RequeueDeadLettersAsync(batch, ct);
                if (result.Count == 0 && result.Message == OperationsService.NoDispatcherMessage)
                    return ([], result.Message);
                return (
                    await DeadLetterFailuresAsync(factory, batch, DeadLetterStatus.Retried, ct),
                    null
                );
            }
            case BulkOperationKind.Acknowledge:
            {
                await services
                    .GetRequiredService<ITraxScheduler>()
                    .AcknowledgeDeadLettersAsync(batch, run.Note!, ct);
                return (
                    await DeadLetterFailuresAsync(
                        factory,
                        batch,
                        DeadLetterStatus.Acknowledged,
                        ct
                    ),
                    null
                );
            }
            case BulkOperationKind.Trigger:
            {
                var result = await services
                    .GetRequiredService<IOperationsService>()
                    .TriggerManifestsAsync(batch, run.AskAfresh, ct);
                if (!result.Success)
                    return ([], result.Message);
                return (await ManifestFailuresAsync(factory, batch, enabled: null, ct), null);
            }
            case BulkOperationKind.Enable:
            case BulkOperationKind.Disable:
            {
                var enabled = run.Kind == BulkOperationKind.Enable;
                var result = await services
                    .GetRequiredService<IOperationsService>()
                    .SetManifestsEnabledAsync(batch, enabled, ct);
                if (!result.Success)
                    return ([], result.Message ?? "The batch was refused.");
                return (await ManifestFailuresAsync(factory, batch, enabled, ct), null);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(run), run.Kind, null);
        }
    }

    private static async Task<IReadOnlyList<BulkOperationFailure>> CancelFailuresAsync(
        IDataContextProviderFactory factory,
        long[] batch,
        CancellationToken ct
    )
    {
        using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db
            .Metadatas.AsNoTracking()
            .Where(m => batch.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                m.TrainState,
                m.CancellationRequested,
                m.InvokingOwnerKind,
            })
            .ToDictionaryAsync(m => m.Id, ct);

        var failures = new List<BulkOperationFailure>();
        foreach (var id in batch)
        {
            if (!rows.TryGetValue(id, out var row))
                failures.Add(new BulkOperationFailure(id, GoneReason));
            else if (row.InvokingOwnerKind == SnapshotOwnerKind.User)
                failures.Add(
                    new BulkOperationFailure(id, OperationsService.UserOwnedRunCancelRefusal)
                );
            else if (!row.CancellationRequested)
                failures.Add(
                    new BulkOperationFailure(
                        id,
                        row.TrainState
                            is TrainState.Completed
                                or TrainState.Failed
                                or TrainState.Cancelled
                            ? $"It had already ended ({Display(row.TrainState)})."
                            : $"It was not cancellable ({Display(row.TrainState)})."
                    )
                );
        }
        return failures;
    }

    private static async Task<IReadOnlyList<BulkOperationFailure>> DeadLetterFailuresAsync(
        IDataContextProviderFactory factory,
        long[] batch,
        DeadLetterStatus wanted,
        CancellationToken ct
    )
    {
        using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db
            .DeadLetters.AsNoTracking()
            .Where(d => batch.Contains(d.Id))
            .Select(d => new { d.Id, d.Status })
            .ToDictionaryAsync(d => d.Id, d => d.Status, ct);

        var failures = new List<BulkOperationFailure>();
        foreach (var id in batch)
        {
            if (!rows.TryGetValue(id, out var status))
                failures.Add(new BulkOperationFailure(id, GoneReason));
            else if (status != wanted)
                failures.Add(
                    new BulkOperationFailure(
                        id,
                        (wanted, status) switch
                        {
                            (DeadLetterStatus.Retried, DeadLetterStatus.AwaitingIntervention) =>
                                "Its manifest already had a queued run, so it was left awaiting intervention.",
                            (DeadLetterStatus.Retried, _) =>
                                "It had been acknowledged, so it was not requeued.",
                            (_, DeadLetterStatus.Retried) =>
                                "It had been requeued, so it was not acknowledged.",
                            _ => "It is still awaiting intervention.",
                        }
                    )
                );
        }
        return failures;
    }

    private static async Task<IReadOnlyList<BulkOperationFailure>> ManifestFailuresAsync(
        IDataContextProviderFactory factory,
        long[] batch,
        bool? enabled,
        CancellationToken ct
    )
    {
        using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db
            .Manifests.AsNoTracking()
            .Where(m => batch.Contains(m.Id))
            .Select(m => new { m.Id, m.IsEnabled })
            .ToDictionaryAsync(m => m.Id, m => m.IsEnabled, ct);

        var failures = new List<BulkOperationFailure>();
        foreach (var id in batch)
        {
            if (!rows.TryGetValue(id, out var isEnabled))
                failures.Add(new BulkOperationFailure(id, GoneReason));
            else if (enabled is { } wanted && isEnabled != wanted)
                failures.Add(
                    new BulkOperationFailure(
                        id,
                        wanted
                            ? "It was disabled again before its batch finished."
                            : "It was enabled again before its batch finished."
                    )
                );
        }
        return failures;
    }

    private static string Display(TrainState state) =>
        state switch
        {
            TrainState.InProgress => "in progress",
            _ => state.ToString().ToLowerInvariant(),
        };
}

/// <summary>
/// The ids a bulk operation will attempt, in the order it attempts them, and the exclusive end
/// index of each batch.
/// </summary>
internal sealed record BulkOperationSnapshot(long[] Ids, int[] BatchEnds);
