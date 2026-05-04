using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    #region Internal junction execution

    /// <summary>
    /// Executes a junction with the provided input and captures its output into Memory.
    /// Used internally by ShortCircuitChain and other dispatch points that need both
    /// the updated monad and the raw junction result.
    /// </summary>
    internal async Task<(
        Monad<TInput, TReturn> Monad,
        Either<Exception, TOut> Result
    )> ChainJunction<TJunction, TIn, TOut>(
        TJunction junction,
        Either<Exception, TIn> previousJunction
    )
        where TJunction : IJunction<TIn, TOut>
    {
        if (Exception is not null)
            return (this, Exception);

        var result = await junction.RailwayJunction(previousJunction, Train).ConfigureAwait(false);

        if (result.IsLeft)
        {
            Exception ??= result.Swap().ValueUnsafe();
        }
        else
        {
            var outValue = result.Unwrap()!;

            if (typeof(TOut).IsTuple())
                this.AddTupleToMemory(outValue);
            else
                Memory[typeof(TOut)] = outValue;
        }

        return (this, result);
    }

    /// <summary>
    /// Executes a junction with input extracted from Memory.
    /// Reflection-invoked from <see cref="Chain{TJunction}(TJunction)"/>.
    /// </summary>
    internal async Task<Monad<TInput, TReturn>> ChainJunction<TJunction, TIn, TOut>(
        TJunction junction
    )
        where TJunction : IJunction<TIn, TOut>
    {
        var input = this.ExtractTypeFromMemory<TIn, TInput, TReturn>();

        if (input is null)
            return this;

        var (monad, _) = await ChainJunction<TJunction, TIn, TOut>(junction, input)
            .ConfigureAwait(false);
        return monad;
    }

    #endregion

    #region Public single-type-arg API

    /// <summary>
    /// Executes a junction that is resolved from Memory by its interface type.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public MonadTask<TInput, TReturn> IChain<TJunction>()
        where TJunction : class => new(IChainAsync<TJunction>());

    private Task<Monad<TInput, TReturn>> IChainAsync<TJunction>()
        where TJunction : class
    {
        var junctionType = typeof(TJunction);

        if (!junctionType.IsInterface)
        {
            Exception ??= new TrainException(
                $"Junction ({junctionType}) must be an interface to call IChain."
            );

            return Task.FromResult(this);
        }

        var junctionService = this.ExtractTypeFromMemory<TJunction, TInput, TReturn>();

        if (junctionService is null)
            return Task.FromResult(this);

        return ChainAsync<TJunction>(junctionService);
    }

    /// <summary>
    /// Creates and executes a junction by its type.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction>()
        where TJunction : class => new(ChainAsync<TJunction>());

    private Task<Monad<TInput, TReturn>> ChainAsync<TJunction>()
        where TJunction : class
    {
        if (Exception is not null)
            return Task.FromResult(this);

        var junctionInstance = this.InitializeJunction<TJunction, TInput, TReturn>();

        if (junctionInstance is null)
            return Task.FromResult(this);

        return ChainAsync<TJunction>(junctionInstance);
    }

    /// <summary>
    /// Executes a junction instance.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction>(TJunction junctionInstance)
        where TJunction : class => new(ChainAsync(junctionInstance));

    private Task<Monad<TInput, TReturn>> ChainAsync<TJunction>(TJunction junctionInstance)
        where TJunction : class
    {
        var (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TJunction>();

        var chainMethod = ReflectionHelpers.FindGenericChainJunctionMethod<
            TJunction,
            TInput,
            TReturn
        >(this, tIn, tOut, 1);

        var result = chainMethod.Invoke(this, [junctionInstance]);

        return (Task<Monad<TInput, TReturn>>)result!;
    }

    #endregion

    #region Public multi-type-arg API (advanced / explicit-typed callers)

    /// <summary>
    /// Executes a junction instance with explicit input/output types.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
        where TJunction : IJunction<TIn, TOut> =>
        new(ChainJunction<TJunction, TIn, TOut>(junction));

    /// <summary>
    /// Creates and executes a junction with explicit input/output types.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() =>
        new(ChainJunction<TJunction, TIn, TOut>(new TJunction()));

    /// <summary>
    /// Executes a junction instance with explicit input type and Unit output.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> =>
        new(ChainJunction<TJunction, TIn, Unit>(junction));

    /// <summary>
    /// Creates and executes a junction with explicit input type and Unit output.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() =>
        new(ChainJunction<TJunction, TIn, Unit>(new TJunction()));

    #endregion
}
