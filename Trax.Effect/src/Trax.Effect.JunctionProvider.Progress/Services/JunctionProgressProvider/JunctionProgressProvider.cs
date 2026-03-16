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

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);
        await serviceTrain.EffectRunner.SaveChanges(cancellationToken);
    }

    public void Dispose() { }
}
