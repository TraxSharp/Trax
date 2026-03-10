using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Train;

namespace Trax.Core.Step;

/// <summary>
/// Base implementation of the IStep interface that provides Railway-oriented programming support.
/// Steps are the fundamental building blocks of trains, each performing a single operation
/// and returning either a result or an exception.
/// </summary>
/// <typeparam name="TIn">The input type for this step</typeparam>
/// <typeparam name="TOut">The output type produced by this step</typeparam>
public abstract class Step<TIn, TOut> : IStep<TIn, TOut>
{
    /// <summary>
    /// Structured exception context when a step fails, including the train name, step name, and error details.
    /// Set automatically by the railway error handler.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TrainExceptionData? ExceptionData { get; private set; }

    /// <summary>
    /// The Either result from the preceding step in the chain.
    /// Left contains an exception from a previous failure; Right contains this step's input.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Either<Exception, TIn> PreviousResult { get; private set; }

    /// <summary>
    /// This step's Either result. Left contains the exception on failure; Right contains the output on success.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Either<Exception, TOut> Result { get; private set; }

    /// <summary>
    /// The CancellationToken for the current train execution.
    /// Set automatically by the train before Run is called.
    /// Step implementations can use this to check for cancellation.
    /// </summary>
    public CancellationToken CancellationToken { get; protected internal set; }

    /// <summary>
    /// The core implementation method that performs the step's operation.
    /// This must be implemented by derived classes.
    /// </summary>
    /// <param name="input">The input data for this step</param>
    /// <returns>The output produced by this step</returns>
    public abstract Task<TOut> Run(TIn input);

    /// <summary>
    /// Railway-oriented implementation that handles the Either monad pattern.
    /// This method:
    /// 1. Short-circuits if the previous step failed (input is Left)
    /// 2. Propagates the train's CancellationToken to this step
    /// 3. Checks for cancellation before executing the step
    /// 4. Attempts to run the step and return Right(result)
    /// 5. Catches any exceptions and returns Left(exception)
    /// 6. Enriches non-cancellation exceptions with step context information
    /// </summary>
    /// <param name="previousOutput">Either a result from the previous step or an exception</param>
    /// <param name="train">Train calling the Step</param>
    /// <returns>Either the result of this step or an exception</returns>
    public virtual async Task<Either<Exception, TOut>> RailwayStep<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    )
    {
        PreviousResult = previousOutput;

        // Propagate the token from the train to this step
        CancellationToken = train.CancellationToken;

        // If the previous step failed, short-circuit and return its exception
        if (previousOutput.IsLeft)
            return previousOutput.Swap().ValueUnsafe();

        // Check cancellation before executing
        CancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Execute the step and return its result as Right
            Result = await Run(previousOutput.ValueUnsafe());

            return Result;
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            // Let cancellation propagate cleanly without wrapping in step exception data
            throw;
        }
        catch (Exception e)
        {
            // Enrich the exception with step information for better debugging
            var messageField = typeof(Exception).GetField(
                "_message",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (messageField is null)
                return e;

            var exceptionData = new TrainExceptionData
            {
                TrainName = train.GetType().Name,
                TrainExternalId = train.ExternalId,
                Step = GetType().Name,
                Type = e.GetType().Name,
                Message = e.Message,
            };

            ExceptionData = exceptionData;

            var serializedMessage = JsonSerializer.Serialize(exceptionData);
            messageField.SetValue(e, serializedMessage);

            // Return the exception as Left
            return e;
        }
    }
}
