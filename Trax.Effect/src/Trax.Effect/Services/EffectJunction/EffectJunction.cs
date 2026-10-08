using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Services.JunctionEffectRunner;
using Trax.Effect.Services.JunctionEvents;
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
    public sealed override Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
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
            await BeforeJunction(serviceTrain.JunctionEffectRunner, serviceTrain, previousOutput);

        Metadata.StartTimeUtc = DateTime.UtcNow;

        // A junction skipped because an earlier one failed did not run, so it is not a step.
        var events = previousOutput.IsRight ? JunctionEventRun.For(serviceTrain.Metadata) : null;
        var position = events is null
            ? -1
            : await JunctionSteps.Started(events, Metadata.Name, Metadata.StartTimeUtc.Value);

        Either<Exception, TOut> result;

        try
        {
            result = await base.RailwayJunction(previousOutput, serviceTrain);
        }
        catch (Exception thrown) when (events is not null)
        {
            // A cancellation of the run's token leaves the junction by throwing.
            await ReportEnded(events, position, serviceTrain, thrown);
            throw;
        }

        Metadata.EndTimeUtc = DateTime.UtcNow;
        Metadata.State = result.State;
        // A Left input skips Run: an earlier junction failed and this one never executed.
        Metadata.HasRan = previousOutput.IsRight;

        if (events is not null)
            await ReportEnded(
                events,
                position,
                serviceTrain,
                result.IsLeft ? result.Swap().ValueUnsafe() : null
            );

        if (serviceTrain.JunctionEffectRunner is not null)
            await AfterJunction(serviceTrain.JunctionEffectRunner, serviceTrain, result);

        return result;
    }

    /// <summary>
    /// Runs the junction effects' <c>BeforeJunctionExecution</c>. When the railway already carries a
    /// failure, this junction is skipped and that failure is what the train reports: an effect that
    /// throws then is logged and the failure stands, as in <see cref="AfterJunction"/>. Before a
    /// junction that will run, an effect that throws fails the train as before.
    /// </summary>
    private async Task BeforeJunction<TTrainIn, TTrainOut>(
        IJunctionEffectRunner runner,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        Either<Exception, TIn> previousOutput
    )
    {
        try
        {
            // The token this junction honours, read here because the base sets CancellationToken
            // only once the effects before it have run. Inside a Parallel branch it is the
            // branch's, so a cancelled sibling stops this branch's effects as well as its work.
            await runner.BeforeJunctionExecution(this, serviceTrain, TokenFor(serviceTrain));
        }
        catch (Exception e) when (previousOutput.IsLeft)
        {
            serviceTrain.Logger?.LogWarning(
                e,
                "A junction effect failed before skipped junction ({JunctionName}) in train ({TrainName}); the earlier failure stands.",
                Metadata?.Name,
                serviceTrain.TrainName
            );
        }
    }

    /// <summary>
    /// Runs the junction effects' <c>AfterJunctionExecution</c>. When the railway already carries a
    /// failure, from this junction or one before it, that failure is what the train reports: an
    /// effect that throws then is logged and the failure stands, rather than the effect's exception
    /// replacing it. After a success, an effect that throws fails the train as before.
    /// </summary>
    private async Task AfterJunction<TTrainIn, TTrainOut>(
        IJunctionEffectRunner runner,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        Either<Exception, TOut> result
    )
    {
        try
        {
            await runner.AfterJunctionExecution(this, serviceTrain, TokenFor(serviceTrain));
        }
        catch (Exception e) when (result.IsLeft)
        {
            serviceTrain.Logger?.LogWarning(
                e,
                "A junction effect failed after junction ({JunctionName}) in train ({TrainName}) had failed; the junction's failure stands.",
                Metadata?.Name,
                serviceTrain.TrainName
            );
        }
    }

    /// <summary>
    /// Whether the junction stopped because it was asked to: the run was cancelled, or, inside a
    /// <c>Parallel</c> branch, the branch was, because a sibling failed. A cancellation nothing
    /// asked for (an <c>HttpClient</c> timeout) is a failure.
    /// </summary>
    private static bool IsCancellation<TTrainIn, TTrainOut>(
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        Exception failure
    ) =>
        serviceTrain.IsRequestedCancellation(failure)
        || (
            failure is OperationCanceledException && TokenFor(serviceTrain).IsCancellationRequested
        );

    /// <summary>
    /// Publishes the junction's end. Publishing cannot change the junction's result: whatever goes
    /// wrong is logged.
    /// </summary>
    private async Task ReportEnded<TTrainIn, TTrainOut>(
        JunctionEventRun events,
        int position,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        Exception? failure
    )
    {
        try
        {
            await JunctionSteps.Ended(
                events,
                position,
                Metadata!.Name,
                Metadata.StartTimeUtc ?? DateTime.UtcNow,
                Metadata.EndTimeUtc ?? DateTime.UtcNow,
                failure,
                failure is not null && IsCancellation(serviceTrain, failure),
                serviceTrain.ServiceProvider,
                serviceTrain.Logger
            );
        }
        catch (Exception e)
        {
            serviceTrain.Logger?.LogWarning(
                e,
                "Could not publish the end of junction ({JunctionName}) in train ({TrainName}); the junction's result stands.",
                Metadata?.Name,
                serviceTrain.TrainName
            );
        }
    }
}
