using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.CancellationRegistry;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Centralizes the "set CancellationRequested + notify CancellationRegistry" pattern
/// used by MetadataPage, MetadataDetailPage, and ManifestGroupDetailPage.
/// </summary>
public static class CancellationHelper
{
    /// <summary>
    /// Requests cancellation for the specified metadata IDs by setting the database flag
    /// and attempting same-server instant cancellation via <see cref="ICancellationRegistry"/>.
    /// </summary>
    /// <returns>The number of metadata records that had cancellation requested.</returns>
    public static async Task<int> CancelTrainsAsync(
        IDataContextProviderFactory factory,
        IServiceProvider serviceProvider,
        IEnumerable<long> metadataIds,
        CancellationToken ct
    )
    {
        var ids = metadataIds.ToList();
        if (ids.Count == 0)
            return 0;

        using var context = await factory.CreateDbContextAsync(ct);
        var count = await context
            .Metadatas.Where(m => ids.Contains(m.Id) && m.TrainState == TrainState.InProgress)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancellationRequested, true), ct);

        var registry = serviceProvider.GetService<ICancellationRegistry>();
        if (registry is not null)
        {
            foreach (var id in ids)
                registry.TryCancel(id);
        }

        return count;
    }
}
