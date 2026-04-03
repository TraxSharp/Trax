using System.ComponentModel;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Train;

namespace Trax.Core.Junction;

/// <summary>
/// Base implementation of the IJunction interface that provides Railway-oriented programming support.
/// Junctions are the fundamental building blocks of trains, each performing a single operation
/// and returning either a result or an exception.
/// </summary>
/// <typeparam name="TIn">The input type for this junction</typeparam>
/// <typeparam name="TOut">The output type produced by this junction</typeparam>
public abstract class Junction<TIn, TOut> : IJunction<TIn, TOut>
{
    /// <summary>
    /// Structured exception context when a junction fails, including the train name, junction name, and error details.
    /// Set automatically by the railway error handler.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TrainExceptionData? ExceptionData { get; private set; }

    /// <summary>
    /// The Either result from the preceding junction in the chain.
    /// Left contains an exception from a previous failure; Right contains this junction's input.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Either<Exception, TIn> PreviousResult { get; private set; }

    /// <summary>
    /// This junction's Either result. Left contains the exception on failure; Right contains the output on success.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Either<Exception, TOut> Result { get; private set; }

    /// <summary>
    /// The CancellationToken for the current train execution.
    /// Set automatically by the train before Run is called.
    /// Junction implementations can use this to check for cancellation.
    /// </summary>
    public CancellationToken CancellationToken { get; protected internal set; }

    /// <summary>
    /// The core implementation method that performs the junction's operation.
    /// This must be implemented by derived classes.
    /// </summary>
    /// <param name="input">The input data for this junction</param>
    /// <returns>The output produced by this junction</returns>
    public abstract Task<TOut> Run(TIn input);

    /// <summary>
    /// Railway-oriented implementation that handles the Either monad pattern.
    /// This method:
    /// 1. Short-circuits if the previous junction failed (input is Left)
    /// 2. Propagates the train's CancellationToken to this junction
    /// 3. Checks for cancellation before executing the junction
    /// 4. Attempts to run the junction and return Right(result)
    /// 5. Catches any exceptions and returns Left(exception)
    /// 6. Enriches non-cancellation exceptions with junction context information
    /// </summary>
    /// <param name="previousOutput">Either a result from the previous junction or an exception</param>
    /// <param name="train">Train calling the Junction</param>
    /// <returns>Either the result of this junction or an exception</returns>
    public virtual async Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    )
    {
        PreviousResult = previousOutput;

        // Propagate the token from the train to this junction
        CancellationToken = train.CancellationToken;

        // If the previous junction failed, short-circuit and return its exception
        if (previousOutput.IsLeft)
            return previousOutput.Swap().ValueUnsafe();

        // Check cancellation before executing
        CancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Execute the junction and return its result as Right
            Result = await Run(previousOutput.ValueUnsafe());

            return Result;
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            // Let cancellation propagate cleanly without wrapping in junction exception data
            throw;
        }
        catch (Exception e)
        {
            var exceptionData = new TrainExceptionData
            {
                TrainName = train.GetType().Name,
                TrainExternalId = train.ExternalId,
                Junction = GetType().Name,
                Type = e.GetType().Name,
                Message = e.Message,
                StackTrace = e.StackTrace,
            };

            ExceptionData = exceptionData;

            // Store structured data on the exception without mutating its message.
            // This preserves the original exception for callers outside Trax,
            // while still making junction context available to Metadata.AddException().
            e.Data["TrainExceptionData"] = exceptionData;

            return e;
        }
    }
}
