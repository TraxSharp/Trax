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
        where TJunction : class
    {
        if (Recorder is null)
            return new(IChainAsync<TJunction>());

        // The runtime refuses a non-interface on every run, so the declaration is refused too.
        if (!typeof(TJunction).IsInterface)
            Recorder.Refuse(
                $"IChain<{typeof(TJunction).Name}> names a class; IChain resolves a junction by "
                    + "its interface. Use Chain with a class."
            );

        return RecordStep<TJunction>(ChainStepKind.IChain);
    }

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
        where TJunction : class =>
        Recorder is not null
            ? RecordStep<TJunction>(ChainStepKind.Chain)
            : new(ChainAsync<TJunction>());

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
        where TJunction : class =>
        Recorder is not null
            ? RecordStep<TJunction>(ChainStepKind.Chain)
            : new(ChainAsync(junctionInstance));

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
        Recorder is not null
            ? RecordStep<TJunction>(ChainStepKind.Chain, typeof(TIn), typeof(TOut))
            : new(ChainJunction<TJunction, TIn, TOut>(junction));

    /// <summary>
    /// Creates and executes a junction with explicit input/output types.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() =>
        Recorder is not null
            ? RecordStep<TJunction>(ChainStepKind.Chain, typeof(TIn), typeof(TOut))
            : new(ChainJunction<TJunction, TIn, TOut>(new TJunction()));

    /// <summary>
    /// Executes a junction instance with explicit input type and Unit output.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> =>
        Recorder is not null
            ? RecordStep<TJunction>(ChainStepKind.Chain, typeof(TIn), typeof(Unit))
            : new(ChainJunction<TJunction, TIn, Unit>(junction));

    /// <summary>
    /// Creates and executes a junction with explicit input type and Unit output.
    /// </summary>
    public MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() =>
        Recorder is not null
            ? RecordStep<TJunction>(ChainStepKind.Chain, typeof(TIn), typeof(Unit))
            : new(ChainJunction<TJunction, TIn, Unit>(new TJunction()));

    #endregion

    /// <summary>
    /// Writes one step to the recorder and hands back a completed monad, so a route reads as a
    /// sequence of types without resolving a junction or running one.
    /// </summary>
    private MonadTask<TInput, TReturn> RecordStep<TJunction>(ChainStepKind kind)
    {
        Type tIn,
            tOut;

        try
        {
            (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TJunction>();
        }
        catch (InvalidOperationException)
        {
            // A type that is not a junction fails every run; reading the chain reports it
            // alongside everything else instead of throwing out of DeclaredChain.
            Recorder!.Refuse(
                $"{kind} names {typeof(TJunction).Name}, which does not implement "
                    + "IJunction<TIn, TOut>."
            );

            return new MonadTask<TInput, TReturn>(Task.FromResult(this));
        }

        Recorder!.Record(kind, typeof(TJunction), tIn, tOut);

        return new MonadTask<TInput, TReturn>(Task.FromResult(this));
    }

    /// <summary>
    /// Records a step whose input and output types the caller stated explicitly, rather than
    /// ones inferred from the junction's interface.
    /// </summary>
    private MonadTask<TInput, TReturn> RecordStep<TJunction>(
        ChainStepKind kind,
        Type tIn,
        Type tOut
    )
    {
        Recorder!.Record(kind, typeof(TJunction), tIn, tOut);

        return new MonadTask<TInput, TReturn>(Task.FromResult(this));
    }
}
