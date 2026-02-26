using Trax.Core.Train;
using LanguageExt;

namespace Trax.Core.Monad;

/// <summary>
/// The composable monadic computation context for Trax.Core.
/// Returned by Train.Activate(), this class provides the fluent Chain/Resolve/Extract API
/// for building Railway-oriented workflows as a sequence of steps.
/// </summary>
/// <typeparam name="TInput">The type of input the owning train accepts</typeparam>
/// <typeparam name="TReturn">The type of result the owning train produces</typeparam>
public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Reference to the owning Train, used by steps for context (CancellationToken, ExternalId, type name).
    /// </summary>
    internal Train<TInput, TReturn> Train { get; }

    /// <summary>
    /// The memory dictionary that stores all objects available to the chain.
    /// This includes inputs, outputs of steps, and services.
    /// Objects are stored by their Type, allowing for type-based retrieval.
    /// </summary>
    internal Dictionary<Type, object> Memory { get; private set; }

    /// <summary>
    /// Gets or sets the exception that occurred during chain execution.
    /// If set, this exception will be returned by Resolve() and will short-circuit further execution.
    /// </summary>
    internal Exception? Exception { get; set; }

    /// <summary>
    /// The CancellationToken for this chain execution.
    /// </summary>
    internal CancellationToken CancellationToken { get; }

    /// <summary>
    /// The value to return when short-circuiting the chain.
    /// </summary>
    private TReturn ShortCircuitValue { get; set; } = default!;

    /// <summary>
    /// Indicates whether a short-circuit value has been set.
    /// </summary>
    private bool ShortCircuitValueSet { get; set; }

    /// <summary>
    /// Creates a Monad for a pure Train (no ServiceProvider).
    /// </summary>
    internal Monad(Train<TInput, TReturn> train, CancellationToken cancellationToken)
    {
        Train = train;
        CancellationToken = cancellationToken;
        Memory = new Dictionary<Type, object> { { typeof(Unit), Unit.Default } };
    }

    /// <summary>
    /// Creates a Monad for a ServiceTrain (with ServiceProvider for step DI).
    /// </summary>
    internal Monad(
        Train<TInput, TReturn> train,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken
    )
    {
        Train = train;
        CancellationToken = cancellationToken;
        Memory = new Dictionary<Type, object>
        {
            { typeof(Unit), Unit.Default },
            { typeof(IServiceProvider), serviceProvider },
        };
    }
}
