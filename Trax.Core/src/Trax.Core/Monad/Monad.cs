using LanguageExt;
using Trax.Core.Train;

namespace Trax.Core.Monad;

/// <summary>
/// The composable monadic computation context for Trax.Core.
/// Created for each run of a train, this class provides the fluent Chain/Resolve/Extract API
/// for building Railway-oriented trains as a sequence of junctions.
/// </summary>
/// <typeparam name="TInput">The type of input the owning train accepts</typeparam>
/// <typeparam name="TReturn">The type of result the owning train produces</typeparam>
public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Reference to the owning Train, used by junctions for context (CancellationToken, ExternalId, type name).
    /// </summary>
    internal Train<TInput, TReturn> Train { get; }

    /// <summary>
    /// The memory dictionary that stores all objects available to the chain.
    /// This includes inputs, outputs of junctions, and services.
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
    internal TReturn ShortCircuitValue { get; set; } = default!;

    /// <summary>
    /// Indicates whether a short-circuit value has been set.
    /// </summary>
    internal bool ShortCircuitValueSet { get; set; }

    /// <summary>
    /// Set when this monad is reading a chain rather than running one. Every chain call then
    /// records its type arguments here and returns without resolving or executing anything.
    /// </summary>
    internal ChainRecorder? Recorder { get; set; }

    /// <summary>
    /// Creates a Monad for a pure Train (no ServiceProvider).
    /// </summary>
    internal Monad(Train<TInput, TReturn> train, CancellationToken cancellationToken)
    {
        Train = train;
        CancellationToken = cancellationToken;
        Memory = new Dictionary<Type, object> { { typeof(Unit), Unit.Default } };
        Recorder = train.ActiveRecorder;
    }

    /// <summary>
    /// Creates a Monad for a ServiceTrain (with ServiceProvider for junction DI).
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
        Recorder = train.ActiveRecorder;
    }
}
