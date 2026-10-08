using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Data.JunctionEvents;

/// <summary>
/// Writes the steps junction events report to <c>trax.junction_run</c>, off the run's path.
/// Registered by <c>AddJunctionEvents</c>.
/// </summary>
/// <remarks>
/// <para>A run only queues a step. One background writer takes the queue in order, in batches,
/// through a short-lived data context of its own, so a write never flushes or inherits what the
/// run's junctions have tracked, and never waits on, or fails, the run. A junction's row is
/// inserted when it starts and updated when it ends; the rows a batch's ends update are read in one
/// query, so a batch costs the same few round trips however many ends it holds.</para>
///
/// <para>When the queue is full (<see cref="Capacity"/> steps waiting) a step is dropped, counted
/// and logged, rather than holding up the run. A batch the database refuses is written again one
/// step at a time, so one bad step (its run deleted meanwhile, say) costs only itself; each step
/// that still fails is logged and dropped. The host's shutdown drains what is queued.</para>
/// </remarks>
internal sealed class JunctionRunWriter
    : IJunctionRunSink,
        IHostedService,
        IAsyncDisposable,
        IDisposable
{
    /// <summary>How many steps may wait to be written before further ones are dropped.</summary>
    internal const int Capacity = 4096;

    private const int BatchSize = 256;

    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly IDataContextProviderFactory _contexts;
    private readonly ILogger<JunctionRunWriter>? _logger;
    private readonly Channel<Item> _queue;
    private readonly CancellationTokenSource _abandon = new();
    private readonly Task _writer;

    private long _dropped;
    private long _droppedSinceReport;
    private int _failing;
    private int _stopped;

    private sealed record Item(
        long MetadataId,
        JunctionEventPayload? Step,
        TaskCompletionSource? Flushed
    );

    public JunctionRunWriter(
        IDataContextProviderFactory contexts,
        ILogger<JunctionRunWriter>? logger = null
    )
    {
        _contexts = contexts;
        _logger = logger;
        _queue = Channel.CreateBounded<Item>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );
        // The writer is often first resolved on a train run's flow. Its loop outlives that run, so
        // it must not carry the run's ambient state (its own log lines would name the run).
        using (ExecutionContext.SuppressFlow())
            _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>Steps dropped because the queue was full, since the writer was created.</summary>
    internal long DroppedSteps => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public void Write(long metadataId, JunctionEventPayload step)
    {
        if (_queue.Writer.TryWrite(new Item(metadataId, step, null)))
            return;

        // A write refused because the writer stopped is not a drop caused by a slow database.
        if (Volatile.Read(ref _stopped) != 0)
            return;

        Interlocked.Increment(ref _dropped);
        if (Interlocked.Increment(ref _droppedSinceReport) == 1)
            _logger?.LogWarning(
                "Junction run writer queue is full ({Capacity} steps): dropping step {Position} "
                    + "({Name}) of run {MetadataId} and further steps until the database catches up.",
                Capacity,
                step.Position,
                step.Name,
                metadataId
            );
    }

    /// <summary>
    /// Completes once every step queued before the call has been written or given up on. For tests
    /// and shutdown; a run never waits on it.
    /// </summary>
    internal async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new Item(0, null, flushed), cancellationToken);
        await flushed.Task.WaitAsync(cancellationToken);
    }

    private async Task WriteLoopAsync()
    {
        var reader = _queue.Reader;
        var batch = new List<Item>(BatchSize);

        try
        {
            while (await reader.WaitToReadAsync(_abandon.Token))
            {
                while (batch.Count < BatchSize && reader.TryRead(out var item))
                    batch.Add(item);

                await WriteBatchAsync(batch.Where(i => i.Step is not null).ToList());

                foreach (var item in batch)
                    item.Flushed?.TrySetResult();

                batch.Clear();

                if (reader.Count == 0)
                {
                    var dropped = Interlocked.Exchange(ref _droppedSinceReport, 0);
                    if (dropped > 0)
                        _logger?.LogWarning(
                            "Junction run writer queue drained after dropping {Dropped} steps.",
                            dropped
                        );
                }
            }
        }
        catch (OperationCanceledException) when (_abandon.IsCancellationRequested)
        {
            // Shutdown gave up waiting for the queue to drain.
        }
        finally
        {
            foreach (var item in batch)
                item.Flushed?.TrySetResult();
            while (reader.TryRead(out var left))
                left.Flushed?.TrySetResult();
        }
    }

    private async Task WriteBatchAsync(IReadOnlyList<Item> steps)
    {
        if (steps.Count == 0)
            return;

        try
        {
            await WriteAsync(steps);
            Recovered();
            return;
        }
        catch (Exception e)
        {
            _logger?.LogDebug(
                e,
                "Writing a batch of {Count} junction steps failed; writing them one at a time.",
                steps.Count
            );
        }

        foreach (var step in steps)
        {
            try
            {
                await WriteAsync([step]);
                Recovered();
            }
            catch (Exception e)
            {
                // One warning per outage; the steps lost inside it are Debug.
                if (Interlocked.Exchange(ref _failing, 1) == 0)
                    _logger?.LogWarning(
                        e,
                        "Could not write step {Position} ({Name}) of run {MetadataId} to "
                            + "trax.junction_run; it is dropped, and so is each step that fails "
                            + "until a write succeeds again. Runs are not affected.",
                        step.Step!.Position,
                        step.Step.Name,
                        step.MetadataId
                    );
                else
                    _logger?.LogDebug(
                        e,
                        "Could not write step {Position} of run {MetadataId}.",
                        step.Step!.Position,
                        step.MetadataId
                    );
            }
        }
    }

    private void Recovered()
    {
        if (Interlocked.Exchange(ref _failing, 0) == 1)
            _logger?.LogInformation("Junction run writer is writing to trax.junction_run again.");
    }

    private async Task WriteAsync(IReadOnlyList<Item> steps)
    {
        using var context = await _contexts.CreateDbContextAsync(CancellationToken.None);

        // Only a junction's end updates a row; every other step is a row of its own. The rows the
        // batch's ends update are read in one query, so a batch costs two round trips whatever its
        // size, rather than one read per end.
        var ends = steps
            .Where(i => Ends(i.Step!))
            .Select(i => (i.MetadataId, i.Step!.Position))
            .ToList();
        var rows = new Dictionary<(long, int), JunctionRun>();

        if (ends.Count > 0)
        {
            var runs = ends.Select(e => e.MetadataId).Distinct().ToList();
            var positions = ends.Select(e => e.Position).Distinct().ToList();
            var wanted = ends.ToHashSet();

            foreach (
                var existing in await context
                    .JunctionRuns.Where(r =>
                        runs.Contains(r.MetadataId) && positions.Contains(r.Position)
                    )
                    .ToListAsync()
            )
                if (wanted.Contains((existing.MetadataId, existing.Position)))
                    rows[(existing.MetadataId, existing.Position)] = existing;
        }

        foreach (var (metadataId, maybeStep, _) in steps)
        {
            var step = maybeStep!;
            var key = (metadataId, step.Position);

            if (!rows.TryGetValue(key, out var row))
            {
                // A start that was dropped leaves its end to write the whole row.
                row = new JunctionRun { MetadataId = metadataId, Position = step.Position };
                context.JunctionRuns.Add(row);
                rows[key] = row;
            }

            Apply(row, step);
        }

        await context.SaveChanges(CancellationToken.None);
    }

    private static bool Ends(JunctionEventPayload step) =>
        step.Kind == JunctionRunKind.Junction && step.State != JunctionRunState.InProgress;

    private static void Apply(JunctionRun row, JunctionEventPayload step)
    {
        row.Kind = step.Kind;
        row.Name = step.Name;
        row.State = step.State;
        row.StartedAt = step.StartedAt;
        row.EndedAt = step.EndedAt;
        row.FailureClass = step.FailureClass;
        row.FailureException = step.FailureException;
        row.QuestionKey = step.QuestionKey;
        row.Answer = step.AnswerWithheld ? null : step.Answer;
        row.Confidence = step.AnswerWithheld ? null : step.Confidence;
        row.Replayed = step.Replayed;
        row.AnswerWithheld = step.AnswerWithheld;
        row.Attempt = step.Attempt;
        row.NameWithheld = step.NameWithheld;
        row.TrackPosition = step.TrackPosition;
        row.NodeId = step.NodeId;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Stops accepting steps and waits for the queued ones to be written. When
    /// <paramref name="cancellationToken"/> fires first, the rest are abandoned.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref _stopped, 1);
        _queue.Writer.TryComplete();

        try
        {
            await _writer.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _abandon.CancelAsync();
            _logger?.LogWarning(
                "Junction run writer stopped before its queue drained; {Remaining} steps were not written.",
                _queue.Reader.Count
            );
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        using (var timeout = new CancellationTokenSource(DisposeDrainTimeout))
            await StopAsync(timeout.Token);

        _abandon.Dispose();
    }

    /// <summary>Stops accepting steps and abandons what is queued, without blocking.</summary>
    public void Dispose()
    {
        Volatile.Write(ref _stopped, 1);
        _queue.Writer.TryComplete();
        if (!_writer.IsCompleted)
            _abandon.Cancel();
    }
}
