using Microsoft.Extensions.Logging;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;

/// <summary>
/// Records on a run's metadata which junction it is executing and since when, in
/// <c>CurrentlyRunningJunction</c> and <c>JunctionStartedAt</c>, so the dashboard and API can show live
/// progress. Registered by <c>AddJunctionProgress</c>; not intended to be constructed directly.
/// </summary>
/// <remarks>Does nothing for a train that has no metadata or no effect runner.</remarks>
internal class JunctionProgressProvider : IJunctionProgressProvider
{
    /// <summary>
    /// Sets <c>CurrentlyRunningJunction</c> to the junction's name and <c>JunctionStartedAt</c> to now (UTC),
    /// and saves the metadata immediately. A failing save propagates and fails the train.
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
        if (serviceTrain.Metadata is null || serviceTrain.EffectRunner is null)
            return;

        serviceTrain.Metadata.CurrentlyRunningJunction = effectJunction.Metadata?.Name;
        serviceTrain.Metadata.JunctionStartedAt = DateTime.UtcNow;

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);
        await serviceTrain.EffectRunner.SaveChanges(cancellationToken);
    }

    /// <summary>
    /// Clears <c>CurrentlyRunningJunction</c> and <c>JunctionStartedAt</c> and saves the metadata. The save
    /// ignores the caller's token and a failing save is only logged as a warning, so neither can change the
    /// junction's result; the train's final write clears both columns again.
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
        if (serviceTrain.Metadata is null || serviceTrain.EffectRunner is null)
            return;

        serviceTrain.Metadata.CurrentlyRunningJunction = null;
        serviceTrain.Metadata.JunctionStartedAt = null;

        // The junction's work has already returned, so this write is bookkeeping about work that
        // happened, not part of it. Neither the caller's token nor a failing write may replace the
        // junction's result: a caller that cancelled while the work finished would otherwise see
        // the run recorded Cancelled (effect/0005 records it Completed), and a database blip would
        // turn finished work into a Failed run a manifest retries. FinishServiceTrain clears these
        // columns again with the outcome, so a skipped write leaves nothing stale behind.
        try
        {
            await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);
            await serviceTrain.EffectRunner.SaveChanges(CancellationToken.None);
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

    /// <summary>Holds no resources; does nothing.</summary>
    public void Dispose() { }
}
