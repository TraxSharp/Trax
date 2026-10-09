using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
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

        if (ClaimJunction(junction) is { } shared)
        {
            Exception = shared;
            return (this, shared);
        }

        var node = Nodes.Next(ChainNodeScope.JunctionKey(typeof(TJunction)));
        ChainGraph.Enter(node, BranchPath);

        if (BranchPath is not null)
        {
            // Where a test's branch runner holds a branch, or fails it, between junctions.
            try
            {
                await Train.BranchRunner.BeforeJunction(BranchPath, node).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Exception ??= e;
                return (this, e);
            }
        }

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
        if (Exception is not null)
            return this;

        if (SkipStep(ChainNodeScope.JunctionKey(typeof(TJunction))))
            return this;

        var input = this.ExtractTypeFromMemory<TIn, TInput, TReturn>(missing =>
            MonadExtensions.NeedsJunctionInput(typeof(TJunction), missing, Train)
        );

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
            Recorder.RefuseStep(
                ChainStepKind.IChain,
                typeof(TJunction),
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

        // A failed train skips the rest of the chain, and skipping includes not resolving the
        // junction or its input: building one runs constructors and container factories for a
        // step that will never run.
        if (Exception is not null)
            return Task.FromResult(this);

        // A resumed run skips the step before resolving anything for it.
        if (SkipStep(ChainNodeScope.JunctionKey(junctionType)))
            return Task.FromResult(this);

        var junctionService = this.ExtractTypeFromMemory<TJunction, TInput, TReturn>(missing =>
            $"IChain<{missing.ReadableName()}> (train '{Train.GetType().ReadableName()}') needs "
            + $"a junction implementing '{missing.ReadableName()}'"
        );

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
            ? RecordBuiltStep<TJunction>(ChainStepKind.Chain)
            : new(ChainAsync<TJunction>());

    private Task<Monad<TInput, TReturn>> ChainAsync<TJunction>()
        where TJunction : class
    {
        if (Exception is not null)
            return Task.FromResult(this);

        // A resumed run skips the step before building its junction.
        if (SkipStep(ChainNodeScope.JunctionKey(typeof(TJunction))))
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
            ? RecordInstance(junctionInstance, RecordStep<TJunction>(ChainStepKind.Chain))
            : new(ChainAsync(junctionInstance));

    private Task<Monad<TInput, TReturn>> ChainAsync<TJunction>(TJunction junctionInstance)
        where TJunction : class
    {
        if (Exception is not null)
            return Task.FromResult(this);

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
            ? RecordInstance(
                junction,
                RecordStep<TJunction>(ChainStepKind.Chain, typeof(TIn), typeof(TOut))
            )
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
            ? RecordInstance(
                junction,
                RecordStep<TJunction>(ChainStepKind.Chain, typeof(TIn), typeof(Unit))
            )
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
    /// Notes the junction instance a step was handed, so two branches of a <c>Parallel</c>
    /// handed the same one can be refused: a junction instance holds the state of its run.
    /// </summary>
    private MonadTask<TInput, TReturn> RecordInstance(
        object? instance,
        MonadTask<TInput, TReturn> recorded
    )
    {
        if (instance is not null)
            Recorder!.NoteInstance(instance);

        return recorded;
    }

    /// <summary>
    /// Writes one step to the recorder and hands back a completed monad, so a route reads as a
    /// sequence of types without resolving a junction or running one.
    /// </summary>
    private MonadTask<TInput, TReturn> RecordStep<TJunction>(ChainStepKind kind) =>
        RecordStep<TJunction>(kind, built: false);

    /// <summary>
    /// Records a step whose junction Trax builds from its constructor, refusing a junction it
    /// cannot build. Only the constructor count is decided here: the arguments come from Memory
    /// and the container as the chain runs, and verification checks them only when asked to.
    /// </summary>
    private MonadTask<TInput, TReturn> RecordBuiltStep<TJunction>(ChainStepKind kind) =>
        RecordStep<TJunction>(kind, built: true);

    private MonadTask<TInput, TReturn> RecordStep<TJunction>(ChainStepKind kind, bool built)
    {
        var junction = typeof(TJunction);

        // A short circuit sets the run's result while the chain runs on, so two branches doing it
        // would race for the result.
        if (kind == ChainStepKind.ShortCircuit && Recorder!.InBranch)
            Recorder.RefuseStep(
                kind,
                junction,
                $"ShortCircuit<{junction.ReadableName()}> is inside a Parallel branch, where it "
                    + "would race its siblings for the run's result. Short-circuit before the "
                    + "Parallel or after it."
            );
        Type tIn,
            tOut;

        try
        {
            (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TJunction>();
        }
        catch (InvalidOperationException)
        {
            // A type that is not a junction fails every run; reading the chain reports it
            // alongside everything else instead of throwing out of DeclaredChain. The step is
            // still recorded, without types, so every later step keeps its written position.
            // Whether such a type could be built is beside the point, so that is not asked.
            Recorder!.RefuseStep(
                kind,
                junction,
                $"{kind} names {junction.Name}, which does not implement IJunction<TIn, TOut>."
            );
            Recorder.Record(kind, junction, null, null);

            return new MonadTask<TInput, TReturn>(Task.FromResult(this));
        }

        if (!built)
            Recorder!.Record(kind, junction, tIn, tOut);
        else if (MonadExtensions.JunctionConstructorProblem(junction) is { } problem)
        {
            Recorder!.RefuseStep(kind, junction, $"{kind}<{junction.ReadableName()}>: {problem}");
            Recorder.Record(kind, junction, tIn, tOut);
        }
        else
            Recorder!.RecordBuilt(kind, junction, tIn, tOut);

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
