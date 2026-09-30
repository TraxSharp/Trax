using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.EffectJunction;

/// <summary>
/// A junction that records <see cref="JunctionMetadata"/> for each run and lets junction effects (the junction
/// logger, junction progress) run before and after it. Derive from it instead of <see cref="Junction{TIn,TOut}"/>
/// when the junction belongs to a <see cref="ServiceTrain{TIn,TOut}"/> and should be observable; it throws
/// <see cref="TrainException"/> when chained into any other kind of train.
/// </summary>
/// <typeparam name="TIn">The junction's input type.</typeparam>
/// <typeparam name="TOut">The junction's output type.</typeparam>
public abstract class EffectJunction<TIn, TOut> : Junction<TIn, TOut>, IEffectJunction<TIn, TOut>
{
    /// <summary>
    /// The core implementation method that performs the junction's operation.
    /// This must be implemented by derived classes.
    /// </summary>
    /// <param name="input">The input data for this junction</param>
    /// <returns>The output produced by this junction</returns>
    public abstract override Task<TOut> Run(TIn input);

    /// <summary>
    /// The in-memory record of the current or most recent run: name, input/output types, start and end times,
    /// the railway state, and <c>OutputJson</c> when the junction logger serializes output. <c>null</c> until the
    /// junction first runs, then replaced at the start of every run. It is not written to the database; junction
    /// effects read it, and the junction logger logs it.
    /// </summary>
    public JunctionMetadata? Metadata { get; private set; }

    /// <summary>
    /// Routes to the <see cref="ServiceTrain{TIn,TOut}"/> overload.
    /// </summary>
    /// <param name="previousOutput">The previous junction's result, or the train input.</param>
    /// <param name="train">The running train; must be a <see cref="ServiceTrain{TIn,TOut}"/>.</param>
    /// <exception cref="TrainException"><paramref name="train"/> is not a <see cref="ServiceTrain{TIn,TOut}"/>.</exception>
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

    /// <summary>
    /// Runs the junction inside a service train: creates a fresh <see cref="Metadata"/>, runs the train's
    /// junction effects' <c>BeforeJunctionExecution</c>, runs the junction on the railway (a
    /// <c>Left</c> input skips <see cref="Run"/> and leaves <c>HasRan</c> false), stamps the end time and state, then runs the effects'
    /// <c>AfterJunctionExecution</c>. Called by the train; not intended to be called directly.
    /// </summary>
    /// <param name="previousOutput">The previous junction's result, or the train input.</param>
    /// <param name="serviceTrain">The running train, whose metadata and junction effect runner are used.</param>
    /// <returns>The junction's result on the railway.</returns>
    /// <exception cref="TrainException">The train's <c>Metadata</c> is <c>null</c>.</exception>
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
        // A Left input skips Run: an earlier junction failed and this one never executed.
        Metadata.HasRan = previousOutput.IsRight;

        if (serviceTrain.JunctionEffectRunner is not null)
            await serviceTrain.JunctionEffectRunner.AfterJunctionExecution(
                this,
                serviceTrain,
                serviceTrain.CancellationToken
            );

        return result;
    }
}
