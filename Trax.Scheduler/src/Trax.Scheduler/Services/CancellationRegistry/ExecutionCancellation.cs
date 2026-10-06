using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ChangeSignal;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Services.CancellationRegistry;

/// <summary>
/// The one rule for cancelling runs, used by <c>IOperationsService.CancelExecutionsAsync</c> and
/// by <c>ITraxScheduler.CancelAsync</c> and <c>CancelGroupAsync</c>, so a selection of runs, a
/// manifest's runs and a group's runs are cancelled the same way (docs/0022).
/// </summary>
internal static class ExecutionCancellation
{
    /// <summary>
    /// The runs of the manifests in <paramref name="groupIds"/>, the candidates of a group cancel.
    /// Shared by <c>ITraxScheduler.CancelGroupAsync</c> and the operations service's batch group
    /// cancel, so the two flag the same runs.
    /// </summary>
    /// <remarks>
    /// The groups' manifests are read first, and the runs are then selected by that list. Given
    /// the groups alone, the database cannot tell how many manifests a thousand ids name (mostly
    /// none, when they come from a caller), guesses hundreds, and reads every active run to join
    /// them; given the manifests, it reads each one's active runs from its index on
    /// <c>(manifest_id, train_state)</c>.
    /// </remarks>
    internal static async Task<IQueryable<Metadata>> InGroupsAsync(
        IDataContext context,
        IReadOnlyCollection<long> groupIds,
        CancellationToken ct
    )
    {
        var manifestIds = await context
            .Manifests.Where(x => groupIds.Contains(x.ManifestGroupId))
            .Select(x => x.Id)
            .ToListAsync(ct);

        return context.Metadatas.Where(m =>
            m.ManifestId != null && manifestIds.Contains(m.ManifestId.Value)
        );
    }

    /// <summary>
    /// Flags every run among <paramref name="candidates"/> that is still <c>Pending</c> or
    /// <c>InProgress</c>, and cancels each one running on this host through the registry.
    /// </summary>
    /// <remarks>
    /// The flag is the durable request, observed at a run's next junction boundary on whatever
    /// host runs it. A Pending run sees it when the job runner picks it up, on any host and
    /// whatever junction providers that host registers: the run is recorded Cancelled and its
    /// train is not run. The flag is set by one statement over the candidates, so its cost is the
    /// rows it writes and never a list of their ids going to the database and back: a group's
    /// cancel can flag tens of thousands of runs. Only the runs this host has registered are read
    /// back, before the update, and only those that were cancellable then go to the registry, so
    /// a finished run's token is never touched. A registry that cannot say which runs it holds is
    /// asked about every cancellable candidate. On a provider without set updates (InMemory) the
    /// same rows are loaded and saved instead. When any run is flagged,
    /// <see cref="ChangeDomain.Execution"/> is signalled, so a runs view refetches without waiting
    /// for the cancellation to take effect.
    /// </remarks>
    /// <returns>The number of runs flagged.</returns>
    internal static async Task<int> RequestAsync(
        IDataContext context,
        IQueryable<Metadata> candidates,
        ICancellationRegistry? registry,
        ITraxChangeSignal? changeSignal,
        CancellationToken ct
    )
    {
        var cancellable = candidates.Where(m =>
            m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress
        );

        List<long> onThisHost = registry switch
        {
            null => [],
            CancellationRegistry known => known.RegisteredIds() is { Count: > 0 } registered
                ? await cancellable
                    .Where(m => registered.Contains(m.Id))
                    .Select(m => m.Id)
                    .ToListAsync(ct)
                : [],
            _ => await cancellable.Select(m => m.Id).ToListAsync(ct),
        };

        var flagged = context.SupportsSetUpdates()
            ? await cancellable.ExecuteUpdateAsync(
                s => s.SetProperty(m => m.CancellationRequested, true),
                ct
            )
            : await context.UpdateEachAsync(cancellable, m => m.CancellationRequested = true, ct);

        foreach (var id in onThisHost)
            registry!.TryCancel(id);

        if (flagged > 0)
            changeSignal?.Notify(ChangeDomain.Execution);

        return flagged;
    }
}
