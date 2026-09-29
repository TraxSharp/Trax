using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;

/// <summary>
/// Stops a run between junctions when cancellation has been requested for it in the database, which is
/// how the dashboard and the API cancel a run that may be executing on another server. Registered by
/// <c>AddJunctionProgress</c>; not intended to be constructed directly.
/// </summary>
/// <remarks>
/// The check happens only before a junction starts: a junction already running is not interrupted by
/// it. Each check opens its own short-lived data context and reads one column.
/// </remarks>
/// <param name="dataContextFactory">Creates the context used to read the run's cancellation flag.</param>
public class CancellationCheckProvider(IDataContextProviderFactory dataContextFactory)
    : ICancellationCheckProvider
{
    /// <summary>
    /// Reads the run's <c>CancellationRequested</c> flag from its metadata row. If it is set, also sets it
    /// on the in-memory metadata, so the run is recorded as cancelled rather than failed, and throws.
    /// Does nothing for a train with no metadata.
    /// </summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">The junction about to run.</param>
    /// <param name="serviceTrain">The train whose run is checked.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <exception cref="OperationCanceledException">Cancellation was requested for the run.</exception>
    public async Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null)
            return;

        await using var context = await dataContextFactory.CreateDbContextAsync(cancellationToken);

        var cancelRequested = await context
            .Metadatas.Where(m => m.Id == serviceTrain.Metadata.Id)
            .Select(m => m.CancellationRequested)
            .FirstOrDefaultAsync(cancellationToken);

        if (cancelRequested)
        {
            // Mirrored onto the run so its outcome is recorded as a requested cancellation. The
            // train's own token was not cancelled, and an OperationCanceledException nothing
            // asked for is recorded as a failure.
            serviceTrain.Metadata.CancellationRequested = true;
            throw new OperationCanceledException("Train cancellation requested via dashboard.");
        }
    }

    /// <summary>Does nothing; the check runs only before a junction.</summary>
    /// <typeparam name="TIn">The junction's input type.</typeparam>
    /// <typeparam name="TOut">The junction's output type.</typeparam>
    /// <typeparam name="TTrainIn">The train's input type.</typeparam>
    /// <typeparam name="TTrainOut">The train's output type.</typeparam>
    /// <param name="effectJunction">Unused.</param>
    /// <param name="serviceTrain">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    public Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    /// <summary>Holds no resources; does nothing.</summary>
    public void Dispose() { }
}
