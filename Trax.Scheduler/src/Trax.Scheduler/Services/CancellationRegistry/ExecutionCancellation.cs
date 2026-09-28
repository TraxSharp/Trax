using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ChangeSignal;

namespace Trax.Scheduler.Services.CancellationRegistry;

/// <summary>
/// The one rule for cancelling runs, used by <c>IOperationsService.CancelExecutionsAsync</c> and
/// by <c>ITraxScheduler.CancelAsync</c> and <c>CancelGroupAsync</c>, so a selection of runs, a
/// manifest's runs and a group's runs are cancelled the same way (docs/0022).
/// </summary>
internal static class ExecutionCancellation
{
    /// <summary>
    /// Flags every run among <paramref name="candidates"/> that is still <c>Pending</c> or
    /// <c>InProgress</c>, and cancels each one running on this host through the registry.
    /// </summary>
    /// <remarks>
    /// The flag is the durable request, observed at a run's next junction boundary on whatever
    /// host runs it; a Pending run sees it when it starts. The update repeats the state test, so
    /// a run that finished between the read and the write is not flagged. Only the runs that
    /// were cancellable when read go to the registry, so a finished run's token is never touched.
    /// When any run is flagged, <see cref="ChangeDomain.Execution"/> is signalled, so a runs view
    /// refetches without waiting for the cancellation to take effect.
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
        var ids = await candidates
            .Where(m => m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (ids.Count == 0)
            return 0;

        var flagged = await context
            .Metadatas.Where(m =>
                ids.Contains(m.Id)
                && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
            )
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancellationRequested, true), ct);

        if (registry is not null)
            foreach (var id in ids)
                registry.TryCancel(id);

        if (flagged > 0)
            changeSignal?.Notify(ChangeDomain.Execution);

        return flagged;
    }
}
