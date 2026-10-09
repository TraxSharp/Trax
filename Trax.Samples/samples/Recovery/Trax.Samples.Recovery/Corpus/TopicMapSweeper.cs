using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// Deletes the topic pairs no <c>topic-map</c> draft points at any more, so the pairs every build
/// writes do not pile up: a rebuild writes a new map and moves the draft's pointer to it, which
/// leaves the old map behind, and a demo run's map is pointed at by nothing once its run has ended.
/// </summary>
/// <remarks>
/// <para>
/// It decides from what the server holds, never from what a client sent: a map is kept while any
/// stored draft's context names it in <c>mapId</c>, and nothing a client writes can make it delete
/// a map. A forged pointer can only keep a map. The build itself does not delete the map it
/// replaces, because the draft's context is the client's to write in the states it may autosave,
/// so a pointer to replace would be one a client chose.
/// </para>
/// <para>
/// A build writes its pairs a moment before its outcome moves the draft's pointer to them, so a
/// map is deleted only when two sweeps, an interval apart, both found it unreferenced. The sweeps
/// remembered are this host's, so a restart only delays a deletion. A draft the user left with
/// Start over stays stored, and so does its map, for as long as the draft does.
/// </para>
/// </remarks>
public sealed class TopicMapSweeper(
    IDbContextFactory<TopicMapDbContext> maps,
    IDataContextProviderFactory trax
)
{
    private readonly object _lock = new();
    private HashSet<string> _unreferencedBefore = new(StringComparer.Ordinal);

    /// <summary>
    /// Deletes the maps the previous sweep found unreferenced and this one still does, and
    /// remembers the rest it found for the next.
    /// </summary>
    /// <returns>The run ids of the maps deleted.</returns>
    public async Task<IReadOnlyList<string>> SweepAsync(CancellationToken cancellationToken)
    {
        var referenced = await ReferencedAsync(cancellationToken);

        await using var db = await maps.CreateDbContextAsync(cancellationToken);
        var written = await db
            .TopicPairs.Select(p => p.RunId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var unreferenced = written.Where(id => !referenced.Contains(id)).ToHashSet();

        List<string> doomed;
        lock (_lock)
        {
            doomed = unreferenced.Where(_unreferencedBefore.Contains).ToList();
            _unreferencedBefore = unreferenced;
            _unreferencedBefore.ExceptWith(doomed);
        }

        if (doomed.Count > 0)
            await db
                .TopicPairs.Where(p => doomed.Contains(p.RunId))
                .ExecuteDeleteAsync(cancellationToken);
        return doomed;
    }

    // Every map a stored topic-map draft points at, in whatever state it is.
    private async Task<HashSet<string>> ReferencedAsync(CancellationToken cancellationToken)
    {
        await using var db = await trax.CreateDbContextAsync(cancellationToken);
        var contexts = await db
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.Machine == TopicMapMachine.MachineId)
            .Select(d => d.Context)
            .ToListAsync(cancellationToken);

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in contexts)
            if (MapIdOf(context) is { } mapId)
                referenced.Add(mapId);
        return referenced;
    }

    private static string? MapIdOf(string context)
    {
        try
        {
            return
                JsonNode.Parse(context)?["mapId"] is JsonValue value
                && value.TryGetValue<string>(out var mapId)
                ? mapId
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary>Runs <see cref="TopicMapSweeper"/> every <see cref="Interval"/>.</summary>
public sealed class TopicMapSweepService(
    TopicMapSweeper sweeper,
    TimeSpan interval,
    ILogger<TopicMapSweepService> logger
) : BackgroundService
{
    /// <summary>How long between sweeps.</summary>
    public TimeSpan Interval => interval;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var deleted = await sweeper.SweepAsync(stoppingToken);
                if (deleted.Count > 0)
                    logger.LogInformation(
                        "Deleted the pairs of {Count} topic map(s) no draft points at",
                        deleted.Count
                    );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The database may be restarting; the next sweep tries again.
                logger.LogWarning(ex, "The topic map sweep failed");
            }
        }
    }
}
