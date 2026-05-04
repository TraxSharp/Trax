using System.Runtime.CompilerServices;
using LanguageExt;
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

    public TaskAwaiter<Monad<TInput, TReturn>> GetAwaiter() => Source.GetAwaiter();

    public ConfiguredTaskAwaitable<Monad<TInput, TReturn>> ConfigureAwait(
        bool continueOnCapturedContext
    ) => Source.ConfigureAwait(continueOnCapturedContext);

    public Task<Monad<TInput, TReturn>> AsTask() => Source;

    public static implicit operator Task<Monad<TInput, TReturn>>(MonadTask<TInput, TReturn> mt) =>
        mt.Source;

    #region Chain

    public MonadTask<TInput, TReturn> Chain<TJunction>()
        where TJunction : class => new(ChainAsync<TJunction>());

    public MonadTask<TInput, TReturn> Chain<TJunction>(TJunction instance)
        where TJunction : class => new(ChainAsync(instance));

    // ReSharper disable once InconsistentNaming
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

    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
        where TJunction : IJunction<TIn, TOut> =>
        new(ChainTypedAsync<TJunction, TIn, TOut>(junction));

    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() =>
        new(ChainTypedAsync<TJunction, TIn, TOut>(new TJunction()));

    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> =>
        new(ChainTypedAsync<TJunction, TIn, Unit>(junction));

    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() =>
        new(ChainTypedAsync<TJunction, TIn, Unit>(new TJunction()));

    private async Task<Monad<TInput, TReturn>> ChainTypedAsync<TJunction, TIn, TOut>(
        TJunction junction
    )
        where TJunction : IJunction<TIn, TOut>
    {
        var monad = await Source.ConfigureAwait(false);
        return await monad.ChainJunction<TJunction, TIn, TOut>(junction).ConfigureAwait(false);
    }

    #endregion

    #region ShortCircuit

    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>()
        where TJunction : class => new(ShortCircuitAsync<TJunction>());

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

    #region Extract

    public MonadTask<TInput, TReturn> Extract<TIn, TOut>() => new(ExtractAsync<TIn, TOut>());

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

    public MonadTask<TInput, TReturn> AddServices<T1>(T1 service) => new(AddServicesAsync(service));

    public MonadTask<TInput, TReturn> AddServices<T1, T2>(T1 s1, T2 s2) =>
        new(AddServicesAsync(s1, s2));

    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3) =>
        new(AddServicesAsync(s1, s2, s3));

    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4) =>
        new(AddServicesAsync(s1, s2, s3, s4));

    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5
    ) => new(AddServicesAsync(s1, s2, s3, s4, s5));

    public MonadTask<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6
    ) => new(AddServicesAsync(s1, s2, s3, s4, s5, s6));

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

    public async Task<Either<Exception, TReturn>> Resolve()
    {
        var monad = await Source.ConfigureAwait(false);
        return monad.Resolve();
    }

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
public static class MonadTaskExtensions
{
    /// <summary>
    /// Converts a <see cref="Task{Monad}"/> into a <see cref="MonadTask{TInput, TReturn}"/>
    /// so the fluent chain can continue with single-type-arg method calls.
    /// </summary>
    public static MonadTask<TInput, TReturn> AsMonadTask<TInput, TReturn>(
        this Task<Monad<TInput, TReturn>> source
    ) => new(source);
}
