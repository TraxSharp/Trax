using System.Runtime.CompilerServices;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;

namespace Trax.Core.Train;

/// <summary>
/// Awaitable wrapper around <see cref="Task{Monad}"/> that preserves the fluent chain
/// surface across async links. Without this, calling <c>.Chain&lt;X&gt;()</c> on a raw
/// <see cref="Task{Monad}"/> would require specifying all three generic type arguments
/// (TJunction, TInput, TReturn) because C# does not perform partial generic inference
/// across explicit type arguments and receiver type. By wrapping in a struct that
/// already knows TInput and TReturn at the type level, calls only need to specify
/// the junction type, matching the Monad&lt;,&gt; instance method shape exactly.
/// </summary>
public readonly struct MonadTask<TInput, TReturn>
{
    internal readonly Task<Monad<TInput, TReturn>> Source;

    internal MonadTask(Task<Monad<TInput, TReturn>> source)
    {
        Source = source;
    }

    /// <summary>
    /// Lets the chain be awaited directly. Awaiting completes when every link queued so far has
    /// run, and yields the underlying monad; a failed junction does not throw here, it is carried
    /// in the monad until <see cref="Resolve()"/>.
    /// </summary>
    public TaskAwaiter<Monad<TInput, TReturn>> GetAwaiter() => Joined().GetAwaiter();

    /// <summary>
    /// Awaits the chain with the given context-capture behaviour, as
    /// <see cref="Task.ConfigureAwait(bool)"/> does for a task.
    /// </summary>
    /// <param name="continueOnCapturedContext">Whether the continuation resumes on the captured context.</param>
    public ConfiguredTaskAwaitable<Monad<TInput, TReturn>> ConfigureAwait(
        bool continueOnCapturedContext
    ) => Joined().ConfigureAwait(continueOnCapturedContext);

    /// <summary>
    /// The task behind this chain, for APIs that take a <see cref="Task{TResult}"/> (such as
    /// <see cref="Task.WhenAll(System.Collections.Generic.IEnumerable{Task})"/>).
    /// </summary>
    public Task<Monad<TInput, TReturn>> AsTask() => Joined();

    /// <summary>
    /// Converts the chain to the task behind it, so a <see cref="MonadTask{TInput, TReturn}"/> can be
    /// returned or passed wherever a <see cref="Task{TResult}"/> of the monad is expected.
    /// </summary>
    /// <param name="mt">The chain to convert.</param>
    public static implicit operator Task<Monad<TInput, TReturn>>(MonadTask<TInput, TReturn> mt) =>
        mt.Joined();

    /// <summary>
    /// The task, noting while a chain is read that the train waits for it, so a statement after
    /// it starts from a finished chain rather than a second one running alongside.
    /// </summary>
    private Task<Monad<TInput, TReturn>> Joined()
    {
        if (Source.IsCompletedSuccessfully)
            Source.Result.Recorder?.NoteJoined();

        return Source;
    }

    #region Chain

    /// <summary>
    /// Queues the junction <typeparamref name="TJunction"/> to run after the links before it;
    /// the asynchronous form of <see cref="Monad{TInput, TReturn}.Chain{TJunction}()"/>. The junction is created
    /// when this link runs, and is skipped if an earlier link failed.
    /// </summary>
    /// <typeparam name="TJunction">The junction class to create and run.</typeparam>
    public MonadTask<TInput, TReturn> Chain<TJunction>()
        where TJunction : class => new(ChainAsync<TJunction>());

    /// <summary>
    /// Queues the given junction instance to run after the links before it; the asynchronous
    /// form of <see cref="Monad{TInput, TReturn}.Chain{TJunction}(TJunction)"/>.
    /// </summary>
    /// <param name="instance">The junction to run. Its input is read from Memory by its declared input type.</param>
    public MonadTask<TInput, TReturn> Chain<TJunction>(TJunction instance)
        where TJunction : class => new(ChainAsync(instance));

    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// Queues a junction resolved by its interface <typeparamref name="TJunction"/> (from Memory,
    /// then the container) to run after the links before it; the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.IChain{TJunction}()"/>. A class type argument fails the chain.
    /// </summary>
    /// <typeparam name="TJunction">The junction interface to resolve.</typeparam>
    public MonadTask<TInput, TReturn> IChain<TJunction>()
        where TJunction : class => new(IChainAsync<TJunction>());

    private async Task<Monad<TInput, TReturn>> ChainAsync<TJunction>()
        where TJunction : class
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Chain<TJunction>().ConfigureAwait(false);
    }

    private async Task<Monad<TInput, TReturn>> ChainAsync<TJunction>(TJunction instance)
        where TJunction : class
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Chain(instance).ConfigureAwait(false);
    }

    private async Task<Monad<TInput, TReturn>> IChainAsync<TJunction>()
        where TJunction : class
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.IChain<TJunction>().ConfigureAwait(false);
    }

    /// <summary>
    /// Queues the given junction with its input and output types stated explicitly; the
    /// asynchronous form of <see cref="Monad{TInput, TReturn}.Chain{TJunction, TIn, TOut}(TJunction)"/>.
    /// </summary>
    /// <param name="junction">The junction to run.</param>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
        where TJunction : IJunction<TIn, TOut> =>
        new(ChainTypedAsync<TJunction, TIn, TOut>(junction));

    /// <summary>
    /// Queues a new <typeparamref name="TJunction"/>, created with its parameterless constructor,
    /// with its input and output types stated explicitly; the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Chain{TJunction, TIn, TOut}()"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() =>
        new(ChainTypedAsync<TJunction, TIn, TOut>(new TJunction()));

    /// <summary>
    /// Queues the given junction, which returns <see cref="Unit"/>, with its input type stated
    /// explicitly; the asynchronous form of <see cref="Monad{TInput, TReturn}.Chain{TJunction, TIn}(TJunction)"/>.
    /// </summary>
    /// <param name="junction">The junction to run.</param>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> =>
        new(ChainTypedAsync<TJunction, TIn, Unit>(junction));

    /// <summary>
    /// Queues a new <typeparamref name="TJunction"/>, which returns <see cref="Unit"/>, created with
    /// its parameterless constructor; the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Chain{TJunction, TIn}()"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() =>
        new(ChainTypedAsync<TJunction, TIn, Unit>(new TJunction()));

    private async Task<Monad<TInput, TReturn>> ChainTypedAsync<TJunction, TIn, TOut>(
        TJunction junction
    )
        where TJunction : IJunction<TIn, TOut>
    {
        var monad = await Source.ConfigureAwait(false);

        // Through the public overload rather than ChainJunction, so a chain being read records
        // this step instead of running the junction.
        return await monad.Chain<TJunction, TIn, TOut>(junction).Source.ConfigureAwait(false);
    }

    #endregion

    #region ShortCircuit

    /// <summary>
    /// Queues <typeparamref name="TJunction"/> as a short-circuit step; the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.ShortCircuit{TJunction}()"/>. A Right result becomes what
    /// <see cref="Resolve()"/> returns and a Left is ignored; later links still run.
    /// </summary>
    /// <typeparam name="TJunction">The junction class to create and run.</typeparam>
    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>()
        where TJunction : class => new(ShortCircuitAsync<TJunction>());

    /// <summary>
    /// Queues the given junction instance as a short-circuit step; the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.ShortCircuit{TJunction}(TJunction)"/>.
    /// </summary>
    /// <param name="instance">The junction to run.</param>
    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>(TJunction instance)
        where TJunction : class => new(ShortCircuitAsync(instance));

    private async Task<Monad<TInput, TReturn>> ShortCircuitAsync<TJunction>()
        where TJunction : class
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.ShortCircuit<TJunction>().ConfigureAwait(false);
    }

    private async Task<Monad<TInput, TReturn>> ShortCircuitAsync<TJunction>(TJunction instance)
        where TJunction : class
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.ShortCircuit(instance).ConfigureAwait(false);
    }

    #endregion

    #region Decisions

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Decide{TState}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Decide<TState>(
        Func<Questions<TState>, Questions<TState>> questions
    ) => new(DecideAsync<TState>(questions));

    private async Task<Monad<TInput, TReturn>> DecideAsync<TState>(
        Func<Questions<TState>, Questions<TState>> questions
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Decide<TState>(questions).ConfigureAwait(false);
    }

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Switch{TTrack}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Switch<TTrack>(
        Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks
    )
        where TTrack : struct, Enum => new(SwitchAsync<TTrack>(tracks));

    private async Task<Monad<TInput, TReturn>> SwitchAsync<TTrack>(
        Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks
    )
        where TTrack : struct, Enum
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Switch<TTrack>(tracks).ConfigureAwait(false);
    }

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Switch{TState, TTrack}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Switch<TState, TTrack>(
        Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks,
        string? asking = null
    )
        where TTrack : struct, Enum => new(SwitchAsync<TState, TTrack>(tracks, asking));

    private async Task<Monad<TInput, TReturn>> SwitchAsync<TState, TTrack>(
        Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks,
        string? asking = null
    )
        where TTrack : struct, Enum
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Switch<TState, TTrack>(tracks, asking).ConfigureAwait(false);
    }

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Gate{TQuestion}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Gate<TQuestion>(
        Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate
    ) => new(GateAsync<TQuestion>(gate));

    private async Task<Monad<TInput, TReturn>> GateAsync<TQuestion>(
        Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Gate<TQuestion>(gate).ConfigureAwait(false);
    }

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Gate{TState, TQuestion}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Gate<TState, TQuestion>(
        Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate,
        string? asking = null
    ) => new(GateAsync<TState, TQuestion>(gate, asking));

    private async Task<Monad<TInput, TReturn>> GateAsync<TState, TQuestion>(
        Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate,
        string? asking = null
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Gate<TState, TQuestion>(gate, asking).ConfigureAwait(false);
    }

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Scale{TLevel}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Scale<TLevel>(
        Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale
    )
        where TLevel : struct, Enum => new(ScaleAsync<TLevel>(scale));

    private async Task<Monad<TInput, TReturn>> ScaleAsync<TLevel>(
        Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale
    )
        where TLevel : struct, Enum
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Scale<TLevel>(scale).ConfigureAwait(false);
    }

    /// <summary>
    /// After the links before it, the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Scale{TState, TLevel}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Scale<TState, TLevel>(
        Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale,
        string? asking = null
    )
        where TLevel : struct, Enum => new(ScaleAsync<TState, TLevel>(scale, asking));

    private async Task<Monad<TInput, TReturn>> ScaleAsync<TState, TLevel>(
        Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale,
        string? asking = null
    )
        where TLevel : struct, Enum
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.Scale<TState, TLevel>(scale, asking).ConfigureAwait(false);
    }

    #endregion

    #region Extract

    /// <summary>
    /// After the links before it, reads the <typeparamref name="TIn"/> in Memory and stores its
    /// first public instance property or field of type <typeparamref name="TOut"/> in Memory; the
    /// asynchronous form of <see cref="Monad{TInput, TReturn}.Extract{TIn, TOut}()"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Extract<TIn, TOut>() => new(ExtractAsync<TIn, TOut>());

    /// <summary>
    /// After the links before it, stores the first public instance property or field of type
    /// <typeparamref name="TOut"/> on <paramref name="input"/> in Memory; the asynchronous form of
    /// <see cref="Monad{TInput, TReturn}.Extract{TIn, TOut}(TIn)"/>.
    /// </summary>
    /// <param name="input">The object to read the value from. Null fails the chain.</param>
    public MonadTask<TInput, TReturn> Extract<TIn, TOut>(TIn input) =>
        new(ExtractAsync<TIn, TOut>(input));

    private async Task<Monad<TInput, TReturn>> ExtractAsync<TIn, TOut>()
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.Extract<TIn, TOut>();
    }

    private async Task<Monad<TInput, TReturn>> ExtractAsync<TIn, TOut>(TIn input)
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.Extract<TIn, TOut>(input);
    }

    #endregion

    #region AddServices

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1}(T1)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="service">The service to store.</param>
    public MonadTask<TInput, TReturn> AddServices<T1>(T1 service) => new(AddServicesAsync(service));

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1, T2}(T1, T2)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="s1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="s2">The service to store under <typeparamref name="T2"/>.</param>
    public MonadTask<TInput, TReturn> AddServices<T1, T2>(T1 s1, T2 s2) =>
        new(AddServicesAsync(s1, s2));

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1, T2, T3}(T1, T2, T3)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="s1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="s2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="s3">The service to store under <typeparamref name="T3"/>.</param>
    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3) =>
        new(AddServicesAsync(s1, s2, s3));

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1, T2, T3, T4}(T1, T2, T3, T4)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="s1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="s2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="s3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="s4">The service to store under <typeparamref name="T4"/>.</param>
    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4) =>
        new(AddServicesAsync(s1, s2, s3, s4));

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1, T2, T3, T4, T5}(T1, T2, T3, T4, T5)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="s1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="s2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="s3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="s4">The service to store under <typeparamref name="T4"/>.</param>
    /// <param name="s5">The service to store under <typeparamref name="T5"/>.</param>
    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5
    ) => new(AddServicesAsync(s1, s2, s3, s4, s5));

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1, T2, T3, T4, T5, T6}(T1, T2, T3, T4, T5, T6)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="s1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="s2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="s3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="s4">The service to store under <typeparamref name="T4"/>.</param>
    /// <param name="s5">The service to store under <typeparamref name="T5"/>.</param>
    /// <param name="s6">The service to store under <typeparamref name="T6"/>.</param>
    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6
    ) => new(AddServicesAsync(s1, s2, s3, s4, s5, s6));

    /// <summary>
    /// After the links before it, stores each service in Memory under the interface it is passed
    /// as; the asynchronous form of <see cref="Monad{TInput, TReturn}.AddServices{T1, T2, T3, T4, T5, T6, T7}(T1, T2, T3, T4, T5, T6, T7)"/>. Each type argument must be an
    /// interface the service implements, and a null service throws when this link runs.
    /// </summary>
    /// <param name="s1">The service to store under <typeparamref name="T1"/>.</param>
    /// <param name="s2">The service to store under <typeparamref name="T2"/>.</param>
    /// <param name="s3">The service to store under <typeparamref name="T3"/>.</param>
    /// <param name="s4">The service to store under <typeparamref name="T4"/>.</param>
    /// <param name="s5">The service to store under <typeparamref name="T5"/>.</param>
    /// <param name="s6">The service to store under <typeparamref name="T6"/>.</param>
    /// <param name="s7">The service to store under <typeparamref name="T7"/>.</param>
    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6,
        T7 s7
    ) => new(AddServicesAsync(s1, s2, s3, s4, s5, s6, s7));

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1>(T1 s1)
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1);
    }

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1, T2>(T1 s1, T2 s2)
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1, s2);
    }

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1, T2, T3>(T1 s1, T2 s2, T3 s3)
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1, s2, s3);
    }

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1, T2, T3, T4>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1, s2, s3, s4);
    }

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1, T2, T3, T4, T5>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1, s2, s3, s4, s5);
    }

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1, T2, T3, T4, T5, T6>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1, s2, s3, s4, s5, s6);
    }

    private async Task<Monad<TInput, TReturn>> AddServicesAsync<T1, T2, T3, T4, T5, T6, T7>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6,
        T7 s7
    )
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.AddServices(s1, s2, s3, s4, s5, s6, s7);
    }

    #endregion

    #region Resolve

    /// <summary>
    /// Waits for every queued link, then returns the train's result: the failure if a link failed,
    /// otherwise the short-circuit value if one was set, otherwise the <typeparamref name="TReturn"/>
    /// in Memory. See <see cref="Monad{TInput, TReturn}.Resolve()"/>.
    /// </summary>
    public async Task<Either<Exception, TReturn>> Resolve()
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.Resolve();
    }

    /// <summary>
    /// Waits for every queued link, then returns the chain's failure if a link failed, otherwise
    /// <paramref name="returnType"/>. See <see cref="Monad{TInput, TReturn}.Resolve(Either{Exception, TReturn})"/>;
    /// not for use in a train's <c>Junctions()</c>.
    /// </summary>
    /// <param name="returnType">The result to return when no link failed.</param>
    public async Task<Either<Exception, TReturn>> Resolve(Either<Exception, TReturn> returnType)
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.Resolve(returnType);
    }

    #endregion
}

/// <summary>
/// Helpers to lift a <see cref="Task{Monad}"/> into a <see cref="MonadTask{TInput, TReturn}"/>
/// for fluent continuation, and to provide implicit-style conversions.
/// </summary>
internal static class MonadTaskExtensions
{
    /// <summary>
    /// Converts a <see cref="Task{Monad}"/> into a <see cref="MonadTask{TInput, TReturn}"/>
    /// so the fluent chain can continue with single-type-arg method calls.
    /// </summary>
    public static MonadTask<TInput, TReturn> AsMonadTask<TInput, TReturn>(
        this Task<Monad<TInput, TReturn>> source
    ) => new(source);
}
