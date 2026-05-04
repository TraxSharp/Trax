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
    /// The CancellationToken for this train execution. Junctions can access this
    /// via their own CancellationToken property which is set before Run is called.
    /// </summary>
    [JsonIgnore]
    public CancellationToken CancellationToken { get; protected internal set; }

    /// <summary>
    /// Internal Monad instance used by the Junctions() API.
    /// Set by the default RunInternal before calling Junctions().
    /// Accessible via internal setter for ServiceTrain to initialize with ServiceProvider.
    /// </summary>
    private Monad<TInput, TReturn>? _monad;

    internal Monad<TInput, TReturn>? TrainMonad
    {
        set => _monad = value;
    }

    /// <summary>
    /// Executes the train with the provided input.
    /// This method unwraps the Either result from RunEither and throws any exceptions.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>The result produced by the train</returns>
    /// <exception cref="Exception">Thrown if any junction in the train fails</exception>
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
    /// Override this for full control over the railway pipeline (advanced).
    /// If not overridden, the default implementation calls Junctions().
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <returns>Either the result of the train or an exception</returns>
    protected virtual async Task<Either<Exception, TReturn>> RunInternal(TInput input)
    {
        _monad = new Monad<TInput, TReturn>(this, CancellationToken).Activate(input);

        try
        {
            return await Junctions().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Defines the train's junction chain. Override this to declare which junctions
    /// the train executes. The chain returns Task&lt;Either&lt;Exception, TReturn&gt;&gt;,
    /// which the default RunInternal awaits and propagates.
    /// </summary>
    /// <returns>The train's railway result</returns>
    protected virtual Task<Either<Exception, TReturn>> Junctions() =>
        throw new NotImplementedException("Override either Junctions() or RunInternal().");

    /// <summary>
    /// Creates a composable Monad helper for chaining junctions.
    /// Used with the RunInternal API. Not needed when overriding Junctions().
    /// </summary>
    /// <param name="input">The primary input for the train</param>
    /// <param name="otherInputs">Additional objects to store in the Monad's Memory</param>
    /// <returns>A Monad instance for method chaining</returns>
    public Monad<TInput, TReturn> Activate(TInput input, params object[] otherInputs) =>
        new Monad<TInput, TReturn>(this, CancellationToken).Activate(input, otherInputs);

    #region Protected chain methods (Junctions API)

    /// <summary>
    /// Creates and executes a junction by its type. Input is extracted from Memory.
    /// </summary>
    protected MonadTask<TInput, TReturn> Chain<TJunction>()
        where TJunction : class => _monad!.Chain<TJunction>();

    /// <summary>
    /// Executes a junction instance. Input is extracted from Memory.
    /// </summary>
    protected MonadTask<TInput, TReturn> Chain<TJunction>(TJunction instance)
        where TJunction : class => _monad!.Chain(instance);

    /// <summary>
    /// Executes a junction resolved from Memory by its interface type.
    /// </summary>
    protected MonadTask<TInput, TReturn> IChain<TJunction>()
        where TJunction : class => _monad!.IChain<TJunction>();

    /// <summary>
    /// Extracts a value of type TOut from an object of type TIn in Memory.
    /// </summary>
    protected Monad<TInput, TReturn> Extract<TIn, TOut>() => _monad!.Extract<TIn, TOut>();

    /// <summary>
    /// Extracts a value of type TOut from the provided TIn object.
    /// </summary>
    protected Monad<TInput, TReturn> Extract<TIn, TOut>(TIn input) =>
        _monad!.Extract<TIn, TOut>(input);

    /// <summary>
    /// Executes a junction with short-circuit behavior.
    /// If the junction produces TReturn, the chain ends early with that value.
    /// </summary>
    protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>()
        where TJunction : class => _monad!.ShortCircuit<TJunction>();

    /// <summary>
    /// Executes a junction instance with short-circuit behavior.
    /// </summary>
    protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>(TJunction instance)
        where TJunction : class => _monad!.ShortCircuit(instance);

    /// <summary>
    /// Adds a service to the chain's Memory for interface-based junction resolution.
    /// </summary>
    protected Monad<TInput, TReturn> AddServices<T1>(T1 service) => _monad!.AddServices(service);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2>(T1 s1, T2 s2) =>
        _monad!.AddServices(s1, s2);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3) =>
        _monad!.AddServices(s1, s2, s3);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4) =>
        _monad!.AddServices(s1, s2, s3, s4);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5
    ) => _monad!.AddServices(s1, s2, s3, s4, s5);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6
    ) => _monad!.AddServices(s1, s2, s3, s4, s5, s6);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6,
        T7 s7
    ) => _monad!.AddServices(s1, s2, s3, s4, s5, s6, s7);

    #endregion
}
