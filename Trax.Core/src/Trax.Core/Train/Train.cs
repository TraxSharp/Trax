using System.Text.Json.Serialization;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Junction;
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
    /// Set before Junctions() is called, whether the chain is being run or read.
    /// </summary>
    private Monad<TInput, TReturn>? _monad;

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
    /// Seeds the chain with the input and runs the junctions the train declares.
    /// </summary>
    /// <remarks>
    /// Private on purpose. A train says which junctions run, and nothing else: an override here
    /// could build its chain imperatively, which would put the chain out of reach of the startup
    /// check that reads every train's declaration before the host serves traffic.
    /// </remarks>
    private async Task<Either<Exception, TReturn>> RunInternal(TInput input)
    {
        _monad = NewMonad().Activate(input);

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
    /// Builds the monad this train chains through. Overridden where the monad needs more than
    /// the train itself, such as a <c>ServiceTrain</c> supplying the container that resolves
    /// junctions.
    /// </summary>
    protected virtual Monad<TInput, TReturn> NewMonad() => new(this, CancellationToken);

    /// <summary>
    /// True while this train's chain is being read rather than run.
    /// </summary>
    /// <remarks>
    /// Per-execution state is unavailable while a chain is read and accessors for it throw, so a
    /// chain cannot come to depend on the value being processed. See
    /// <see cref="ChainDeclarationException"/>.
    /// </remarks>
    public bool IsDeclaringChain { get; private set; }

    /// <summary>
    /// The recorder a chain is being read into, so that a monad created while declaring, by
    /// <see cref="NewMonad"/> as much as by the train itself, records instead of running.
    /// </summary>
    internal ChainRecorder? ActiveRecorder { get; private set; }

    /// <summary>
    /// Reads this train's declared chain without resolving or running any junction.
    /// </summary>
    /// <remarks>
    /// Every chain call answers by recording its type arguments, so the result is the sequence
    /// of types the train declares. Nothing touches the container and no junction executes.
    ///
    /// <para><c>Junctions()</c> itself does run, which is why it has to be a pure declaration. A
    /// body that awaits before returning, or that returns a result instead of ending in
    /// <c>Resolve()</c>, has no chain to read, and that is recorded in
    /// <see cref="ChainRecorder.Refusals"/> rather than reported as a clean train. Whatever such a
    /// body started is not undone.</para>
    /// </remarks>
    /// <exception cref="ChainDeclarationException">
    /// The train read per-execution state while declaring its chain.
    /// </exception>
    public ChainRecorder DeclaredChain()
    {
        var recorder = new ChainRecorder();
        ActiveRecorder = recorder;
        IsDeclaringChain = true;

        _monad = NewMonad();

        try
        {
            var declared = Junctions();

            if (!declared.IsCompleted)
            {
                // Every step completes synchronously while recording, so a task still running
                // here is waiting on something the body started itself. Its outcome belongs to
                // no one, so it is observed and dropped rather than left to surface unobserved.
                _ = declared.ContinueWith(
                    t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted
                        | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );

                recorder.Refuse(
                    "Junctions() awaited something before returning, so it does work instead of "
                        + "declaring a chain. Declare the chain without awaiting and move the work "
                        + "into a junction."
                );
            }
            else
            {
                // An async body's exceptions, ChainDeclarationException included, land in the
                // task rather than propagating, so they are rethrown here.
                var result = declared.GetAwaiter().GetResult();

                if (!result.IsLeft || result.Swap().ValueUnsafe() is not ChainRecordedException)
                    recorder.Refuse(
                        "Junctions() returned a result instead of ending its chain with "
                            + "Resolve(), so there is no chain to verify. Chain the junction that "
                            + "produces the result and end with Resolve()."
                    );
            }
        }
        finally
        {
            IsDeclaringChain = false;
            ActiveRecorder = null;
            _monad = null;
        }

        return recorder;
    }

    /// <summary>
    /// Defines the train's junction chain. Override this to declare which junctions
    /// the train executes. The chain returns Task&lt;Either&lt;Exception, TReturn&gt;&gt;,
    /// which the train awaits and propagates when it runs.
    /// </summary>
    /// <returns>The train's railway result</returns>
    protected virtual Task<Either<Exception, TReturn>> Junctions() =>
        throw new NotImplementedException("Override Junctions() to declare this train's chain.");

    /// <summary>
    /// Builds a monad seeded with the input, for code that needs one directly.
    /// </summary>
    /// <remarks>
    /// Internal on purpose. A train declares its chain through <c>Junctions()</c>; handing out a
    /// seeded monad would let a caller build a chain imperatively, which is what keeps a chain
    /// out of reach of the startup check. Trax's own tests use it to exercise the monad itself.
    /// </remarks>
    internal Monad<TInput, TReturn> Activate(TInput input, params object[] otherInputs) =>
        NewMonad().Activate(input, otherInputs);

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
    /// Executes a junction instance whose input and output types are stated explicitly, for
    /// chains where they cannot be inferred from the junction's interface.
    /// </summary>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
        where TJunction : IJunction<TIn, TOut> => _monad!.Chain<TJunction, TIn, TOut>(junction);

    /// <inheritdoc cref="Chain{TJunction,TIn,TOut}(TJunction)"/>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() => _monad!.Chain<TJunction, TIn, TOut>();

    /// <inheritdoc cref="Chain{TJunction,TIn,TOut}(TJunction)"/>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> => _monad!.Chain<TJunction, TIn>(junction);

    /// <inheritdoc cref="Chain{TJunction,TIn,TOut}(TJunction)"/>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() => _monad!.Chain<TJunction, TIn>();

    /// <summary>
    /// Ends a chain that declares no junctions, taking the train's return value from Memory.
    /// </summary>
    /// <remarks>
    /// A train whose return type is already in Memory, because it is the input type or the
    /// seeded <c>Unit</c>, declares a chain of zero junctions. This is the terminal step for
    /// that chain. It takes no value and computes nothing: a declaration states which junctions
    /// run, and stating a result directly would make the chain depend on something other than
    /// the junctions it names.
    /// </remarks>
    protected Either<Exception, TReturn> Resolve() => _monad!.Resolve();

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
    /// Executes a junction with short-circuit behavior. If the junction returns Right, its
    /// TReturn value becomes the train's result; if it returns Left, the failure is ignored.
    /// </summary>
    /// <remarks>
    /// A short circuit does not end the chain: the junctions after it still run, and a failure
    /// in one of them still fails the train. What it decides is what <c>Resolve()</c> returns,
    /// which is the short-circuited value in preference to anything later in Memory. A Right
    /// output is also stored in Memory, but a Left stores nothing, so the chain check does not
    /// count a short circuit's output as available to the junctions after it.
    /// </remarks>
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
