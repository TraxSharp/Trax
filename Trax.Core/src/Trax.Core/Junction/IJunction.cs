using LanguageExt;
using Trax.Core.Train;

namespace Trax.Core.Junction;

/// <summary>
/// Defines the contract for a junction in a train.
/// Junctions are the fundamental building blocks of trains, each performing a single operation
/// and returning either a result or an exception.
/// </summary>
/// <typeparam name="TIn">The input type for this junction</typeparam>
/// <typeparam name="TOut">The output type produced by this junction</typeparam>
public interface IJunction<TIn, TOut>
{
    /// <summary>
    /// Executes the junction's operation with the provided input.
    /// This is the core implementation method that derived classes must implement.
    /// </summary>
    /// <param name="input">The input data for this junction</param>
    /// <returns>The output produced by this junction</returns>
    public Task<TOut> Run(TIn input);

    /// <summary>
    /// Railway-oriented implementation that handles the Either monad pattern.
    /// This method enables the Railway-oriented programming pattern where operations
    /// can be chained together with automatic error handling.
    /// </summary>
    /// <param name="previousOutput">Either a result from the previous junction or an exception</param>
    /// <param name="train">The Train executing this junction</param>
    /// <returns>Either the result of this junction or an exception</returns>
    public Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        Train<TTrainIn, TTrainOut> train
    );
}
