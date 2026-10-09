using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;

/// <summary>
/// Records on a run's metadata which junction it is executing and since when, in
/// <c>CurrentlyRunningJunction</c> and <c>JunctionStartedAt</c>, so the dashboard and API can show live
/// progress. Registered by <c>AddJunctionProgress</c>; not intended to be constructed directly.
/// </summary>
/// <remarks>
/// <para>Does nothing for a train that has no metadata.</para>
/// <para>The two columns are written through a context of their own, never through the run's
/// effect runner. Saving through the runner would commit everything else the run has tracked so
/// far, so a train that failed later would leave its earlier writes behind, and every junction would
/// pay for a full save of every provider. A run's own writes commit once, when it finishes.</para>
/// <para>Junctions in the <c>Parallel</c> branches of one run run side by side, and the run has one
/// pair of columns. They show the junction started last of those still running, so a junction
/// that ends while a sibling's runs hands the columns back to the sibling's rather than clearing
/// them.</para>
/// <para>One writer makes the writes, one at a time, each with what is running when it starts, so
/// the last write to land is the one that is current. A change made while a write is in flight
/// waits for the next one, and every change made in the meantime shares it: however many
/// branches start or end junctions together, a caller waits for at most the write in flight and
/// the one after it, never for a queue of writes. The run settles the writer before its terminal
/// write, which clears both columns, so no write outlives the run.</para>
/// </remarks>
internal class JunctionProgressProvider(IDataContextProviderFactory dataContextFactory)
    : IJunctionProgressProvider,
        ISettlesBeforeTheRunEnds
{
    /// <summary>Guards every field below.</summary>
    private readonly object _gate = new();

    /// <summary>
    /// The run's junctions that have started and not yet ended, in the order they started. More
    /// than one only while <c>Parallel</c> branches run.
    /// </summary>
    private readonly List<(object Junction, string? Name, DateTime StartedAt)> _running = [];

    /// <summary>The run's metadata, whose row the writer updates.</summary>
    private Models.Metadata.Metadata? _metadata;

    /// <summary>
    /// The write that carries the changes made since the one in flight read what is running, or
    /// null when there are none.
    /// </summary>
    private TaskCompletionSource? _next;

    /// <summary>Whether the writer is running, and so will pick up <see cref="_next"/>.</summary>
    private bool _writing;

    /// <summary>The writer, while <see cref="_writing"/>.</summary>
    private Task _writer = Task.CompletedTask;

    /// <summary>
    /// Sets <c>CurrentlyRunningJunction</c> to the junction's name and <c>JunctionStartedAt</c> to now (UTC),
    /// and waits until a write carrying them has landed, so the columns show the junction before it
    /// runs. A failing write propagates and fails the train.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction about to run.</param>
    /// <param name="serviceTrain">The train whose metadata is updated.</param>
    /// <param name="cancellationToken">
    /// Stops the wait. The write itself goes on, and the run settles it before it ends.
    /// </param>
    public async Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null)
            return;

        Task written;
        lock (_gate)
        {
            _running.Add((effectJunction, effectJunction.Metadata?.Name, DateTime.UtcNow));
            written = Request(serviceTrain.Metadata);
        }

        try
        {
            await written.WaitAsync(cancellationToken);
        }
        catch
        {
            // The junction fails before it runs, so nothing calls the after half for it, and a
            // sibling branch's next write must not show it as running.
            lock (_gate)
                _running.RemoveAll(r => ReferenceEquals(r.Junction, effectJunction));
            throw;
        }
    }

    /// <summary>
    /// Clears <c>CurrentlyRunningJunction</c> and <c>JunctionStartedAt</c>, or hands them to a junction
    /// still running in a sibling branch, and asks the writer to write them. It does not wait for
    /// the write, and a failing write is only logged as a warning, so neither can change the
    /// junction's result; the train's terminal write clears both columns again.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction that ran.</param>
    /// <param name="serviceTrain">The train whose metadata is updated.</param>
    /// <param name="cancellationToken">Not used for the write, deliberately.</param>
    public Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null)
            return Task.CompletedTask;

        Task written;
        lock (_gate)
        {
            _running.RemoveAll(r => ReferenceEquals(r.Junction, effectJunction));
            written = Request(serviceTrain.Metadata);
        }

        // The junction's work has already returned, so this write is bookkeeping about work that
        // happened, not part of it. Neither the caller's token nor a failing write may replace the
        // junction's result: a caller that cancelled while the work finished would otherwise see
        // the run recorded Cancelled (effect/0005 records it Completed), and a database blip would
        // turn finished work into a Failed run a manifest retries. The run's terminal write always
        // includes these columns, cleared (DataContext.Update marks them modified for a run that
        // has ended), so a write that fails here, or an after half that never runs because the
        // junction was cancelled, leaves nothing stale behind.
        var logger = serviceTrain.Logger;
        var trainName = serviceTrain.TrainName;
        var junctionName = effectJunction.Metadata?.Name;
        _ = written.ContinueWith(
            failed =>
                logger?.LogWarning(
                    failed.Exception?.InnerException,
                    "Could not clear the junction progress of train ({TrainName}) after junction ({JunctionName}); the junction's result stands.",
                    trainName,
                    junctionName
                ),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );

        return Task.CompletedTask;
    }

    /// <summary>
    /// Completes once the writer has nothing left to write. The run calls it before its terminal
    /// write, after every junction has returned, so nothing new is asked for meanwhile.
    /// </summary>
    public async Task Settle()
    {
        while (true)
        {
            Task writer;
            lock (_gate)
            {
                if (!_writing)
                    return;
                writer = _writer;
            }

            await writer;
        }
    }

    /// <summary>
    /// Asks for a write of what is running now, and starts the writer if it is idle. The returned
    /// task completes when a write that began after this call has landed. Called under
    /// <see cref="_gate"/>.
    /// </summary>
    private Task Request(Models.Metadata.Metadata metadata)
    {
        _metadata = metadata;

        // The run's own copy shows the change at once, to anything in this process that reads it.
        var (junction, startedAt) = Current();
        metadata.CurrentlyRunningJunction = junction;
        metadata.JunctionStartedAt = startedAt;

        _next ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = _next.Task;

        if (!_writing)
        {
            _writing = true;
            _writer = Task.Run(WriteUntilCurrent);
        }

        return written;
    }

    /// <summary>
    /// Writes until no change is waiting. Each write reads what is running when it starts and
    /// completes every request made before that. It never throws: a failed write fails the
    /// requests it carried.
    /// </summary>
    private async Task WriteUntilCurrent()
    {
        while (true)
        {
            TaskCompletionSource carried;
            long metadataId;
            string? junction;
            DateTime? startedAt;

            lock (_gate)
            {
                if (_next is null)
                {
                    _writing = false;
                    return;
                }

                carried = _next;
                _next = null;
                (junction, startedAt) = Current();
                metadataId = _metadata!.Id;
            }

            try
            {
                // Not a caller's token: the write carries every caller's change, and the run
                // settles it before it ends.
                await Write(metadataId, junction, startedAt, CancellationToken.None);
                carried.SetResult();
            }
            catch (Exception ex)
            {
                carried.SetException(ex);
            }
        }
    }

    /// <summary>
    /// The junction the columns show: the one started last of those still running. Called under
    /// <see cref="_gate"/>.
    /// </summary>
    private (string? Junction, DateTime? StartedAt) Current() =>
        _running.Count > 0 ? (_running[^1].Name, _running[^1].StartedAt) : (null, null);

    /// <summary>
    /// Writes the run's two progress columns, and only those, to its row. A row not saved yet has
    /// nothing to write to.
    /// </summary>
    private async Task Write(
        long metadataId,
        string? junction,
        DateTime? startedAt,
        CancellationToken cancellationToken
    )
    {
        if (metadataId <= 0)
            return;

        await using var context = await dataContextFactory.CreateDbContextAsync(cancellationToken);

        if (context is DbContext db && db.Database.IsRelational())
        {
            await context
                .Metadatas.Where(m => m.Id == metadataId)
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(m => m.CurrentlyRunningJunction, junction)
                            .SetProperty(m => m.JunctionStartedAt, startedAt),
                    cancellationToken
                );
            return;
        }

        // The in-memory provider has no bulk update, so the row is read into this context and saved.
        var row = await context.Metadatas.FirstOrDefaultAsync(
            m => m.Id == metadataId,
            cancellationToken
        );

        if (row is null)
            return;

        row.CurrentlyRunningJunction = junction;
        row.JunctionStartedAt = startedAt;
        await context.SaveChanges(cancellationToken);
    }

    /// <summary>
    /// Holds nothing to release: the writer settles before the run ends, and each write disposes
    /// its own context.
    /// </summary>
    public void Dispose() { }
}
