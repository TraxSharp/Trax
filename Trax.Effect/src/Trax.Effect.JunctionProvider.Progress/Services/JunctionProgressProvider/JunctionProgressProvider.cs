using Microsoft.Extensions.Logging;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;

public class JunctionProgressProvider : IJunctionProgressProvider
{
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

    public void Dispose() { }
}
