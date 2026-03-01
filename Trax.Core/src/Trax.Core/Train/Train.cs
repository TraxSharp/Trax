using System.Text.Json.Serialization;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Extensions;
using Trax.Core.Monad;
using Trax.Core.Route;

namespace Trax.Core.Train;

/// <summary>
/// Base class for all trains in Trax.Core.
/// A train traverses a route, processing an input and producing a result.
/// This class provides the core functionality for execution, including
/// Railway-oriented programming support via the Monad helper.
/// </summary>
/// <typeparam name="TInput">The type of input the train accepts</typeparam>
/// <typeparam name="TReturn">The type of result the train produces</typeparam>
public abstract class Train<TInput, TReturn> : IRoute<TInput, TReturn>
{
    public string ExternalId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The CancellationToken for this train execution. Steps can access this
    /// via their own CancellationToken property which is set before Run is called.
    /// </summary>
    [JsonIgnore]
    public CancellationToken CancellationToken { get; protected internal set; }

    /// <summary>
    /// Executes the train with the provided input.
    /// This method unwraps the Either result from RunEither and throws any exceptions.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>The result produced by the train</returns>
    /// <exception cref="Exception">Thrown if any step in the train fails</exception>
    public virtual async Task<TReturn> Run(
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        CancellationToken = cancellationToken;

        var resultEither = await RunEither(input);

        if (resultEither.IsLeft)
            resultEither.Swap().ValueUnsafe().Rethrow();

        return resultEither.Unwrap();
    }

    /// <summary>
    /// Executes the train with Railway-oriented programming support.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <returns>Either the result of the train or an exception</returns>
    public Task<Either<Exception, TReturn>> RunEither(TInput input) => RunInternal(input);

    /// <summary>
    /// The core implementation method that executes the train's logic.
    /// This must be implemented by derived classes to define the train's behavior.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <returns>Either the result of the train or an exception</returns>
    protected abstract Task<Either<Exception, TReturn>> RunInternal(TInput input);

    /// <summary>
    /// Creates a composable Monad helper for chaining steps.
    /// This is typically the first method called in a train's RunInternal implementation.
    /// </summary>
    /// <param name="input">The primary input for the train</param>
    /// <param name="otherInputs">Additional objects to store in the Monad's Memory</param>
    /// <returns>A Monad instance for method chaining</returns>
    protected internal Monad<TInput, TReturn> Activate(TInput input, params object[] otherInputs) =>
        new Monad<TInput, TReturn>(this, CancellationToken).Activate(input, otherInputs);
}
