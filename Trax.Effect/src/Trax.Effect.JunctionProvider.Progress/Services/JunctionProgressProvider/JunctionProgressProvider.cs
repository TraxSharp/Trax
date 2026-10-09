using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectJunction;
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
/// them. The writes are made one at a time, each with what is running when it is made, so the
/// last write to land is the one that is current.</para>
/// </remarks>
internal class JunctionProgressProvider(IDataContextProviderFactory dataContextFactory)
    : IJunctionProgressProvider
{
    /// <summary>
    /// Sets <c>CurrentlyRunningJunction</c> to the junction's name and <c>JunctionStartedAt</c> to now (UTC),
    /// and writes the two columns immediately. A failing write propagates and fails the train.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction about to run.</param>
    /// <param name="serviceTrain">The train whose metadata is updated.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null)
            return;

        lock (_running)
            _running.Add((effectJunction, effectJunction.Metadata?.Name, DateTime.UtcNow));

        try
        {
            await Write(serviceTrain.Metadata, cancellationToken);
        }
        catch
        {
            // The junction fails before it runs, so nothing calls the after half for it, and a
            // sibling branch's next write must not show it as running.
            lock (_running)
                _running.RemoveAll(r => ReferenceEquals(r.Junction, effectJunction));
            throw;
        }
    }

    /// <summary>
    /// Clears <c>CurrentlyRunningJunction</c> and <c>JunctionStartedAt</c> and writes the two columns. The
    /// write ignores the caller's token and a failing write is only logged as a warning, so neither can
    /// change the junction's result; the train's final write clears both columns again.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction that ran.</param>
    /// <param name="serviceTrain">The train whose metadata is updated.</param>
    /// <param name="cancellationToken">Not used for the save, deliberately.</param>
    public async Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null)
            return;

        lock (_running)
            _running.RemoveAll(r => ReferenceEquals(r.Junction, effectJunction));

        // The junction's work has already returned, so this write is bookkeeping about work that
        // happened, not part of it. Neither the caller's token nor a failing write may replace the
        // junction's result: a caller that cancelled while the work finished would otherwise see
        // the run recorded Cancelled (effect/0005 records it Completed), and a database blip would
        // turn finished work into a Failed run a manifest retries. The run's terminal write always
        // includes these columns, cleared (DataContext.Update marks them modified for a run that
        // has ended), so a skipped write here, or an after half that never runs because the
        // junction was cancelled, leaves nothing stale behind.
        try
        {
            await Write(serviceTrain.Metadata, CancellationToken.None);
        }
        catch (Exception ex)
        {
            serviceTrain.Logger?.LogWarning(
                ex,
                "Could not clear the junction progress of train ({TrainName}) after junction ({JunctionName}); the junction's result stands.",
                serviceTrain.TrainName,
                effectJunction.Metadata?.Name
            );
        }
    }

    /// <summary>
    /// Writes the run's two progress columns, and only those, to its row. A row not saved yet has
    /// nothing to write to.
    /// </summary>
    /// <summary>
    /// The run's junctions that have started and not yet ended, in the order they started. More
    /// than one only while <c>Parallel</c> branches run.
    /// </summary>
    private readonly List<(object Junction, string? Name, DateTime StartedAt)> _running = [];

    /// <summary>Makes the writes one at a time, so they land in the order they were made.</summary>
    private readonly SemaphoreSlim _writing = new(1, 1);

    private async Task Write(Models.Metadata.Metadata metadata, CancellationToken cancellationToken)
    {
        await _writing.WaitAsync(cancellationToken);
        try
        {
            string? junction = null;
            DateTime? startedAt = null;

            lock (_running)
                if (_running.Count > 0)
                    (_, junction, startedAt) = _running[^1];

            metadata.CurrentlyRunningJunction = junction;
            metadata.JunctionStartedAt = startedAt;

            await Write(metadata.Id, junction, startedAt, cancellationToken);
        }
        finally
        {
            _writing.Release();
        }
    }

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

    /// <summary>Holds no resources; does nothing.</summary>
    public void Dispose() => _writing.Dispose();
}
