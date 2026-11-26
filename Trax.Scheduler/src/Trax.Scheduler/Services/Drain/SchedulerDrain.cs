using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.Drain;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Scheduler.Services.Drain;

/// <summary>
/// The check every part of the scheduler that starts work makes before it starts any: the job
/// dispatcher before a cycle, the manifest manager before it queues scheduled runs, and a local
/// worker inside its claim. A host with no data provider is never drained.
/// </summary>
internal static class SchedulerDrain
{
    /// <summary>Whether a drain is in force now, read from the database through a fresh context.</summary>
    public static async Task<bool> IsDrainedAsync(IServiceProvider services, CancellationToken ct)
    {
        var factory = services.GetService<IDataContextProviderFactory>();
        if (factory is null)
            return false;
        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var disposal = context.ConfigureAwait(false);
        return await DrainStore
            .IsDrainedAsync(context, clock.GetUtcNow(), ct)
            .ConfigureAwait(false);
    }
}
