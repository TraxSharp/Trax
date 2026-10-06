using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Scheduling.E2E.Utilities;

/// <summary>
/// Reads the host's Trax tables and waits on them. Every read opens a fresh data context, so it
/// sees what the scheduler committed rather than a cached entity.
/// </summary>
/// <remarks>
/// Every wait owns its ceiling (Trax.Docs ADR 0014): the budget's token is passed to each query,
/// so a stalled query cannot outlive the wait and report a state that never arrived.
/// </remarks>
public sealed class Db(IServiceProvider services)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public async Task<T> Query<T>(
        Func<IDataContext, Task<T>> query,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dataContext = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            return await query(dataContext);
        }
        finally
        {
            (dataContext as IDisposable)?.Dispose();
        }
    }

    public Task<Manifest> Manifest(string externalId) =>
        Query(dc => dc.Manifests.AsNoTracking().SingleAsync(m => m.ExternalId == externalId));

    /// <summary>The highest metadata id so far: a marker that later runs are newer than.</summary>
    public Task<long> LatestMetadataId() =>
        Query(dc => dc.Metadatas.AsNoTracking().MaxAsync(m => (long?)m.Id))
            .ContinueWith(t => t.Result ?? 0);

    public Task<List<Metadata>> Runs(long manifestId, long afterId) =>
        Query(dc =>
            dc.Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifestId && m.Id > afterId)
                .OrderBy(m => m.Id)
                .ToListAsync()
        );

    /// <summary>Waits for <paramref name="count"/> runs of a manifest, newer than <paramref name="afterId"/>, to reach <paramref name="state"/>.</summary>
    public async Task<List<Metadata>> WaitForRuns(
        long manifestId,
        TrainState state,
        int count,
        long afterId,
        TimeSpan timeout
    )
    {
        List<Metadata> runs = [];
        using var budget = new CancellationTokenSource(timeout);

        var reached = await Polling.WaitUntilAsync(
            async () =>
            {
                if (budget.IsCancellationRequested)
                    return false;
                try
                {
                    runs = await Query(
                        dc =>
                            dc.Metadatas.AsNoTracking()
                                .Where(m =>
                                    m.ManifestId == manifestId
                                    && m.Id > afterId
                                    && m.TrainState == state
                                )
                                .OrderBy(m => m.Id)
                                .ToListAsync(budget.Token),
                        budget.Token
                    );
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                return runs.Count >= count;
            },
            timeout,
            PollInterval
        );

        if (!reached)
        {
            var all = await Runs(manifestId, afterId);
            Assert.Fail(
                $"Expected {count} {state} run(s) of manifest {manifestId} after metadata {afterId} "
                    + $"within {timeout.TotalSeconds}s; saw "
                    + $"[{string.Join(", ", all.Select(r => $"{r.Id}:{r.TrainState}"))}]."
            );
        }

        return runs.Take(count).ToList();
    }

    public async Task<Metadata> WaitForRun(
        long manifestId,
        TrainState state,
        long afterId,
        TimeSpan timeout
    ) => (await WaitForRuns(manifestId, state, 1, afterId, timeout))[0];

    /// <summary>Waits for a dead letter of the manifest, newer than <paramref name="afterId"/>, in <paramref name="status"/>.</summary>
    public async Task<DeadLetter> WaitForDeadLetter(
        long manifestId,
        DeadLetterStatus status,
        TimeSpan timeout,
        long afterId = 0
    )
    {
        DeadLetter? deadLetter = null;
        using var budget = new CancellationTokenSource(timeout);

        var reached = await Polling.WaitUntilAsync(
            async () =>
            {
                if (budget.IsCancellationRequested)
                    return false;
                try
                {
                    deadLetter = await Query(
                        dc =>
                            dc.DeadLetters.AsNoTracking()
                                .Where(d =>
                                    d.ManifestId == manifestId
                                    && d.Id > afterId
                                    && d.Status == status
                                )
                                .OrderByDescending(d => d.Id)
                                .FirstOrDefaultAsync(budget.Token),
                        budget.Token
                    );
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                return deadLetter is not null;
            },
            timeout,
            PollInterval
        );

        reached
            .Should()
            .BeTrue(
                $"manifest {manifestId} should have a {status} dead letter after {afterId} within {timeout.TotalSeconds}s"
            );
        return deadLetter!;
    }

    /// <summary>Waits until <paramref name="condition"/> holds, failing the test with <paramref name="because"/> when it never does.</summary>
    public async Task WaitUntil(Func<Task<bool>> condition, TimeSpan timeout, string because)
    {
        var reached = await Polling.WaitUntilAsync(condition, timeout, PollInterval);
        reached.Should().BeTrue(because);
    }
}
