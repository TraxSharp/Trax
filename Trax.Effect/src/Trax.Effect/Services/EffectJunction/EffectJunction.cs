using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.EffectJunction;

public abstract class EffectJunction<TIn, TOut> : Junction<TIn, TOut>, IEffectJunction<TIn, TOut>
{
    /// <summary>
    /// The core implementation method that performs the junction's operation.
    /// This must be implemented by derived classes.
    /// </summary>
    /// <param name="input">The input data for this junction</param>
    /// <returns>The output produced by this junction</returns>
    public abstract override Task<TOut> Run(TIn input);

    public JunctionMetadata? Metadata { get; private set; }

    public override Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    )
    {
        if (train is not ServiceTrain<TTrainIn, TTrainOut> serviceTrain)
            throw new TrainException(
                $"Cannot run an EffectJunction ({GetType().Name}) against a non-ServiceTrain ({train.GetType().Name})"
            );

        return RailwayJunction(previousOutput, serviceTrain);
    }

    public async Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    )
    {
        if (serviceTrain.Metadata is null)
            throw new TrainException(
                "ServiceTrain Metadata cannot be null. Something has gone horribly wrong."
            );

        Metadata = JunctionMetadata.Create(
            new CreateJunctionMetadata
            {
                Name = GetType().Name,
                ExternalId = Guid.NewGuid().ToString("N"),
                InputType = typeof(TIn),
                OutputType = typeof(TOut),
                State = previousOutput.State,
            },
            serviceTrain.Metadata
        );

        if (serviceTrain.JunctionEffectRunner is not null)
            await serviceTrain.JunctionEffectRunner.BeforeJunctionExecution(
                this,
                serviceTrain,
                serviceTrain.CancellationToken
            );

        Metadata.StartTimeUtc = DateTime.UtcNow;

        var result = await base.RailwayJunction(previousOutput, serviceTrain);

        Metadata.EndTimeUtc = DateTime.UtcNow;
        Metadata.State = result.State;
        Metadata.HasRan = true;

        if (serviceTrain.JunctionEffectRunner is not null)
            await serviceTrain.JunctionEffectRunner.AfterJunctionExecution(
                this,
                serviceTrain,
                serviceTrain.CancellationToken
            );

        return result;
    }
}
