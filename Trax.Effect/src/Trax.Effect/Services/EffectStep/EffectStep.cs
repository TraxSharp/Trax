using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Step;
using Trax.Core.Train;
using Trax.Effect.Models.StepMetadata;
using Trax.Effect.Models.StepMetadata.DTOs;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.EffectStep;

public abstract class EffectStep<TIn, TOut> : Step<TIn, TOut>, IEffectStep<TIn, TOut>
{
    /// <summary>
    /// The core implementation method that performs the step's operation.
    /// This must be implemented by derived classes.
    /// </summary>
    /// <param name="input">The input data for this step</param>
    /// <returns>The output produced by this step</returns>
    public abstract override Task<TOut> Run(TIn input);

    public StepMetadata? Metadata { get; private set; }

    public override Task<Either<Exception, TOut>> RailwayStep<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    )
    {
        if (train is not ServiceTrain<TTrainIn, TTrainOut> serviceTrain)
            throw new TrainException(
                $"Cannot run an EffectStep ({GetType().Name}) against a non-ServiceTrain ({train.GetType().Name})"
            );

        return RailwayStep(previousOutput, serviceTrain);
    }

    public async Task<Either<Exception, TOut>> RailwayStep<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    )
    {
        if (serviceTrain.Metadata is null)
            throw new TrainException(
                "ServiceTrain Metadata cannot be null. Something has gone horribly wrong."
            );

        Metadata = StepMetadata.Create(
            new CreateStepMetadata
            {
                Name = GetType().Name,
                ExternalId = Guid.NewGuid().ToString("N"),
                InputType = typeof(TIn),
                OutputType = typeof(TOut),
                State = previousOutput.State,
            },
            serviceTrain.Metadata
        );

        if (serviceTrain.StepEffectRunner is not null)
            await serviceTrain.StepEffectRunner.BeforeStepExecution(
                this,
                serviceTrain,
                serviceTrain.CancellationToken
            );

        Metadata.StartTimeUtc = DateTime.UtcNow;

        var result = await base.RailwayStep(previousOutput, serviceTrain);

        Metadata.EndTimeUtc = DateTime.UtcNow;
        Metadata.State = result.State;
        Metadata.HasRan = true;

        if (serviceTrain.StepEffectRunner is not null)
            await serviceTrain.StepEffectRunner.AfterStepExecution(
                this,
                serviceTrain,
                serviceTrain.CancellationToken
            );

        return result;
    }
}
