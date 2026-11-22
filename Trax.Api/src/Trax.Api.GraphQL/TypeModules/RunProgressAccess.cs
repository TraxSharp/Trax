using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.Types;
using Trax.Core.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Finds a run the current caller started, of a train that lets its starter follow it: the latest run under the id, or
/// the queued entry when none has run yet. Everything else is not found, through one query per table, so a missing id
/// and another caller's take the same path.
/// </summary>
internal sealed class RunProgressAccess(
    ITrainDiscoveryService discovery,
    IDataContextProviderFactory contexts
)
{
    private readonly Lazy<
        IReadOnlyDictionary<string, (TrainRegistration Train, string? Field)>
    > _trains = new(() =>
    {
        // Every train that opts in, exposed or not: a scheduled or invoked train has no field of its own, but its
        // starter follows its runs all the same.
        var trains = discovery.DiscoverTrains();
        var fields = TrainTypeModule.AssignTrainNames(trains);
        return trains
            .Where(t => t.IsRunProgressEnabled)
            .ToDictionary(
                t => t.ServiceType.FullName.NotNull(),
                t =>
                    (
                        t,
                        fields.TryGetValue(t, out var field)
                            ? TrainTypeModule.CamelCase(field)
                            : (string?)null
                    ),
                StringComparer.Ordinal
            );
    });

    public Task<RunProgressRun?> FindAsync(
        string runId,
        IServiceProvider services,
        CancellationToken ct
    ) => FindAsync(runId, Starters(services), ct);

    /// <summary>The run <paramref name="runId"/> when one of <paramref name="starters"/> started it.</summary>
    public async Task<RunProgressRun?> FindAsync(
        string runId,
        IReadOnlyCollection<string> starters,
        CancellationToken ct
    )
    {
        if (starters.Count == 0 || runId.Length != 32 || _trains.Value.Count == 0)
            return null;
        var names = _trains.Value.Keys.ToList();

        var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var dbDisposal = db.ConfigureAwait(false);
        var ran = await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ExternalId == runId
                && m.StarterHash != null
                && starters.Contains(m.StarterHash)
                && names.Contains(m.Name)
            )
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.TrainState,
                m.FailureException,
                m.FailureReason,
                m.StarterHash,
                m.ManifestId,
                m.EndTime,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (ran is not null)
        {
            var found = Run(
                runId,
                ran.Name,
                ran.Id,
                StateOf(ran.TrainState),
                string.Equals(ran.FailureException, "TrainException", StringComparison.Ordinal)
                    ? ran.FailureReason
                    : null,
                ran.StarterHash.NotNull()
            ) with
            {
                ManifestId = ran.ManifestId,
                EndedAt = ran.EndTime,
            };
            return found.State == RunProgressState.Failed && found.ManifestId is not null
                ? found with
                {
                    RetriedAs = (await NextTryAsync(found, ct).ConfigureAwait(false)).RetriedAs,
                }
                : found;
        }

        return await FindQueuedAsync(db, runId, starters, names, ct).ConfigureAwait(false);
    }

    /// <summary>The run <paramref name="runId"/> while it is still queued for one of <paramref name="starters"/>.</summary>
    private async Task<RunProgressRun?> FindQueuedAsync(
        Trax.Effect.Data.Services.DataContext.IDataContext db,
        string runId,
        IReadOnlyCollection<string> starters,
        List<string> names,
        CancellationToken ct
    )
    {
        var queued = await db
            .WorkQueues.AsNoTracking()
            .Where(w =>
                w.ExternalId == runId
                && w.Status == WorkQueueStatus.Queued
                && w.StarterHash != null
                && starters.Contains(w.StarterHash)
                && names.Contains(w.TrainName)
            )
            .Select(w => new { w.TrainName, w.StarterHash })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return queued is null
            ? null
            : Run(
                runId,
                queued.TrainName,
                metadataId: null,
                RunProgressState.Queued,
                failure: null,
                queued.StarterHash.NotNull()
            );
    }

    /// <summary>
    /// What became of <paramref name="run"/>, first found queued: its latest entry's status for the same starter, or,
    /// with no entry, whether a run was recorded under its id.
    /// </summary>
    public async Task<QueuedFate> QueuedFateAsync(RunProgressRun run, CancellationToken ct)
    {
        var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var dbDisposal = db.ConfigureAwait(false);
        var status = await db
            .WorkQueues.AsNoTracking()
            .Where(w => w.ExternalId == run.RunId && w.StarterHash == run.Starter)
            .OrderByDescending(w => w.Id)
            .Select(w => (WorkQueueStatus?)w.Status)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return status switch
        {
            WorkQueueStatus.Queued => QueuedFate.Waiting,
            WorkQueueStatus.Cancelled => QueuedFate.Cancelled,
            WorkQueueStatus.Dispatched => QueuedFate.Dispatched,
            _ => await db
                .Metadatas.AsNoTracking()
                .AnyAsync(m => m.ExternalId == run.RunId && m.StarterHash == run.Starter, ct)
                .ConfigureAwait(false)
                ? QueuedFate.Dispatched
                : QueuedFate.Gone,
        };
    }

    /// <summary>
    /// For a failed run of a manifest: the run that retries it, once queued, and whether no retry will come because
    /// the manifest was dead-lettered after it or is disabled. A retry is the manifest's next run or queued entry for
    /// the same starter.
    /// </summary>
    public async Task<(string? RetriedAs, bool NoRetry)> NextTryAsync(
        RunProgressRun run,
        CancellationToken ct
    )
    {
        var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var dbDisposal = db.ConfigureAwait(false);

        // A run first seen while queued knows neither its metadata nor its manifest yet: its row has them now.
        var ran = await db
            .Metadatas.AsNoTracking()
            .Where(m => m.ExternalId == run.RunId && m.StarterHash == run.Starter)
            .OrderByDescending(m => m.Id)
            .Select(m => new
            {
                m.Id,
                m.ManifestId,
                m.EndTime,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (ran?.ManifestId is not { } manifestId)
            return (null, ran is not null);
        var metadataId = ran.Id;
        var next = await db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ManifestId == manifestId && m.Id > metadataId && m.StarterHash == run.Starter
            )
            .OrderBy(m => m.Id)
            .Select(m => m.ExternalId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (next is not null)
            return (next, false);

        var queued = await db
            .WorkQueues.AsNoTracking()
            .Where(w =>
                w.ManifestId == manifestId
                && w.Status == WorkQueueStatus.Queued
                && w.StarterHash == run.Starter
            )
            .OrderBy(w => w.Id)
            .Select(w => w.ExternalId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (queued is not null)
            return (queued, false);

        // A disabled manifest is not retried, so the stream need not wait out RetryWait for it.
        var disabled = await db
            .Manifests.AsNoTracking()
            .AnyAsync(m => m.Id == manifestId && !m.IsEnabled, ct)
            .ConfigureAwait(false);
        if (disabled)
            return (null, true);

        var since = ran.EndTime ?? run.EndedAt ?? DateTime.MinValue;
        var deadLettered = await db
            .DeadLetters.AsNoTracking()
            .AnyAsync(d => d.ManifestId == manifestId && d.DeadLetteredAt >= since, ct)
            .ConfigureAwait(false);
        return (null, deadLettered);
    }

    private RunProgressRun Run(
        string runId,
        string trainName,
        long? metadataId,
        RunProgressState state,
        string? failure,
        string starter
    )
    {
        var (train, field) = _trains.Value[trainName];
        var visibility = new LifecycleVisibility(
            All: false,
            new HashSet<string>(StringComparer.Ordinal) { trainName }
        )
        {
            JunctionAnswers = train.RunProgressAnswers,
        };
        return new RunProgressRun(
            runId,
            trainName,
            field,
            metadataId,
            state,
            failure,
            starter,
            visibility
        );
    }

    /// <summary>
    /// The caller of <paramref name="services"/> as a run records its starter: the principal id the mediator read when
    /// the caller started it, and the user key a machine records when it invokes the train for its owner.
    /// </summary>
    internal static List<string> Starters(IServiceProvider services)
    {
        var principalId = services.GetService<ICurrentPrincipalProvider>()?.GetCurrentPrincipalId();
        var userKey = services.GetService<ISnapshotPrincipal>()?.CurrentUserKey;
        return new[] { RunStarter.OfPrincipal(principalId), RunStarter.OfUserKey(userKey) }
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal static RunProgressState StateOf(TrainState state) =>
        state switch
        {
            TrainState.Pending => RunProgressState.Pending,
            TrainState.InProgress => RunProgressState.InProgress,
            TrainState.Completed => RunProgressState.Completed,
            TrainState.Failed => RunProgressState.Failed,
            _ => RunProgressState.Cancelled,
        };
}
