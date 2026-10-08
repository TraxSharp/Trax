using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
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
/// <remarks>
/// A supported base for code that uses Trax.Core on its own. <see cref="Run"/> stays virtual, and
/// a subclass that overrides it without calling <c>base.Run</c> runs something other than its
/// declared chain, so <see cref="DeclaredChain"/>, and any check built on it, no longer describes
/// what runs. A <c>ServiceTrain</c> cannot do this: its <c>Run</c> is sealed. See
/// Trax.Core ADR 0003.
/// </remarks>
/// <typeparam name="TInput">The type of input the train accepts</typeparam>
/// <typeparam name="TReturn">The type of result the train produces</typeparam>
public abstract class Train<TInput, TReturn> : IRoute<TInput, TReturn>
{
    /// <summary>
    /// Identifies one run of this train across logs, stored metadata and failure data
    /// (<see cref="Exceptions.TrainExceptionData.TrainExternalId"/>). Defaults to a new GUID in
    /// 32-digit <c>"N"</c> format when the train is constructed; set it before <see cref="Run"/> to
    /// correlate the run with an id you already have.
    /// </summary>
    /// <remarks>
    /// A <c>ServiceTrain</c> run again on the same instance gets a fresh id unless the caller set a
    /// new one first.
    /// </remarks>
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
    /// Starts this train's <c>Parallel</c> branches. Always the thread pool in a shipped host;
    /// Trax's own tests substitute a runner that controls how branches interleave.
    /// </summary>
    internal IBranchRunner BranchRunner { get; set; } = ThreadPoolBranchRunner.Instance;

    /// <summary>
    /// What the next run restores and where it starts, when it resumes an earlier run of the same
    /// input; null for a run from the top. Set by the host before <see cref="Run"/>.
    /// </summary>
    internal ResumePlan? Resume { get; set; }

    /// <summary>
    /// Executes the train with the provided input.
    /// This method unwraps the Either result from RunEither and throws any exceptions.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>The result produced by the train</returns>
    /// <exception cref="Exception">Thrown if any junction in the train fails</exception>
    /// <remarks>
    /// Virtual because the published Trax.Effect overrides it in <c>ServiceTrain</c>. An override
    /// here that does not call <c>base.Run</c> skips <see cref="Junctions"/>, so the chain check
    /// reads a chain that never runs (Trax.Core ADR 0003).
    /// </remarks>
    public virtual async Task<TReturn> Run(
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        CancellationToken = cancellationToken;

        var resultEither = await RunEither(input);

        if (resultEither.IsLeft)
            ExceptionDispatchInfo.Capture(resultEither.Swap().ValueUnsafe()).Throw();

        return resultEither.Unwrap();
    }

    /// <summary>
    /// Executes the train with Railway-oriented programming support.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <returns>
    /// Either the result of the train or an exception. Nothing a junction throws escapes this
    /// method, cancellation included: a cancelled run returns Left holding the
    /// <see cref="OperationCanceledException"/>, which a caller tells apart with
    /// <c>is OperationCanceledException</c>. The token is the one <see cref="Run"/> was given, or
    /// the one the host set on <see cref="CancellationToken"/>.
    /// </returns>
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
        var monad = _monad = NewMonad().Activate(input);

        if (Resume is { } resume)
        {
            if (resume.Restored.GetValueOrDefault("") is { } restored)
                monad.Restore(restored);

            if (resume.Target is { } target)
                monad.Resuming = (target, resume.Inclusive);
        }

        try
        {
            return await Junctions().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            // A branch's scope outlives its branch: what it put in Memory may hold on to it.
            foreach (var scope in monad.BranchScopes)
            {
                try
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // A branch's scope failing to dispose cannot change the run's result.
                }
            }
        }
    }

    /// <summary>
    /// Builds the monad this train chains through. Overridden where the monad needs more than
    /// the train itself, such as a <c>ServiceTrain</c> supplying the container that resolves
    /// junctions.
    /// </summary>
    /// <remarks>
    /// The seam <c>ServiceTrain</c> uses, not a consumer extension point:
    /// <see cref="Monad{TInput, TReturn}"/> has no public constructor, so an override outside Trax
    /// can only return a monad a base implementation built. It stays <c>protected</c> because the published Trax.Effect overrides it as
    /// <c>protected override</c>, and narrowing it would make every <c>ServiceTrain</c> fail to
    /// load against a newer Trax.Core (Trax.Core ADR 0002).
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
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
    /// of types the train declares. No junction is resolved or executes. The container is asked only
    /// for each decider a decision step names, so one that vets questions
    /// (<see cref="Trax.Core.Decisions.IVetsQuestions"/>) can refuse what it cannot answer; none is asked to
    /// decide.
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
    /// <exception cref="Exception">
    /// Whatever <c>Junctions()</c> throws while it is read, such as the base
    /// <see cref="NotImplementedException"/> of a train that declares no chain, is thrown from here
    /// rather than recorded as a refusal.
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

    /// <summary>
    /// The train's monad, for a chain call made directly on the train. While the chain is read,
    /// the call is noted so a second, unlinked chain is refused.
    /// </summary>
    private Monad<TInput, TReturn> Root(string call, bool startsAJunction)
    {
        // Inside a Parallel branch, a call on the train runs on the run's own Memory beside the
        // branch rather than in it.
        if (_monad?.Recorder is { InBranch: true } branch)
            branch.Refuse(
                $"a Parallel branch calls {call} on the train itself, which runs on the run's "
                    + "Memory beside the branch instead of in it. Chain it on the branch's "
                    + "parameter, as in b => b.Chain<A>()."
            );

        ActiveRecorder?.NoteRootCall(call, startsAJunction);
        return _monad!;
    }

    #region Protected chain methods (Junctions API)

    /// <summary>
    /// Creates and executes a junction by its type. Input is extracted from Memory.
    /// </summary>
    protected MonadTask<TInput, TReturn> Chain<TJunction>()
        where TJunction : class => Root("Chain", true).Chain<TJunction>();

    /// <summary>
    /// Executes a junction instance. Input is extracted from Memory.
    /// </summary>
    protected MonadTask<TInput, TReturn> Chain<TJunction>(TJunction instance)
        where TJunction : class => Root("Chain", true).Chain(instance);

    /// <summary>
    /// Executes a junction resolved from Memory by its interface type.
    /// </summary>
    protected MonadTask<TInput, TReturn> IChain<TJunction>()
        where TJunction : class => Root("IChain", true).IChain<TJunction>();

    /// <summary>
    /// Executes a junction instance whose input and output types are stated explicitly, for
    /// chains where they cannot be inferred from the junction's interface.
    /// </summary>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
        where TJunction : IJunction<TIn, TOut> =>
        Root("Chain", true).Chain<TJunction, TIn, TOut>(junction);

    /// <inheritdoc cref="Chain{TJunction,TIn,TOut}(TJunction)"/>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() =>
        Root("Chain", true).Chain<TJunction, TIn, TOut>();

    /// <inheritdoc cref="Chain{TJunction,TIn,TOut}(TJunction)"/>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> =>
        Root("Chain", true).Chain<TJunction, TIn>(junction);

    /// <inheritdoc cref="Chain{TJunction,TIn,TOut}(TJunction)"/>
    protected MonadTask<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() =>
        Root("Chain", true).Chain<TJunction, TIn>();

    /// <summary>
    /// Runs a fixed set of named branches side by side, each on its own copy of Memory, and
    /// joins them before the next step. See <see cref="Monad{TInput, TReturn}.Parallel"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalIds.Parallel)]
    protected MonadTask<TInput, TReturn> Parallel(
        Func<Branches<TInput, TReturn>, Branches<TInput, TReturn>> branches
    ) =>
#pragma warning disable TRAXEXP001 // The experimental feature's own entry point.
        Root("Parallel", true).Parallel(branches);
#pragma warning restore TRAXEXP001

    /// <summary>
    /// Stores the state in Memory so a later run of the same input can resume here. See
    /// <see cref="Monad{TInput, TReturn}.Checkpoint{TState}"/>.
    /// </summary>
    protected MonadTask<TInput, TReturn> Checkpoint<TState>() =>
        Root("Checkpoint", true).Checkpoint<TState>();

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
    protected Either<Exception, TReturn> Resolve() => Root("Resolve", false).Resolve();

    /// <summary>
    /// Extracts a value of type TOut from an object of type TIn in Memory.
    /// </summary>
    protected Monad<TInput, TReturn> Extract<TIn, TOut>() =>
        Root("Extract", false).Extract<TIn, TOut>();

    /// <summary>
    /// Extracts a value of type TOut from the provided TIn object.
    /// </summary>
    protected Monad<TInput, TReturn> Extract<TIn, TOut>(TIn input) =>
        Root("Extract", false).Extract<TIn, TOut>(input);

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
        where TJunction : class => Root("ShortCircuit", true).ShortCircuit<TJunction>();

    /// <summary>
    /// Executes a junction instance with short-circuit behavior.
    /// </summary>
    protected MonadTask<TInput, TReturn> ShortCircuit<TJunction>(TJunction instance)
        where TJunction : class => Root("ShortCircuit", true).ShortCircuit(instance);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Decide{TState}"/>
    protected MonadTask<TInput, TReturn> Decide<TState>(
        Func<Decisions.Questions<TState>, Decisions.Questions<TState>> questions
    ) => Root("Decide", true).Decide<TState>(questions);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Switch{TTrack}"/>
    protected MonadTask<TInput, TReturn> Switch<TTrack>(
        Func<
            Decisions.Tracks<TInput, TReturn, TTrack>,
            Decisions.Tracks<TInput, TReturn, TTrack>
        > tracks
    )
        where TTrack : struct, Enum => Root("Switch", true).Switch<TTrack>(tracks);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Switch{TState, TTrack}"/>
    protected MonadTask<TInput, TReturn> Switch<TState, TTrack>(
        Func<
            Decisions.Tracks<TInput, TReturn, TTrack>,
            Decisions.Tracks<TInput, TReturn, TTrack>
        > tracks,
        string? asking = null
    )
        where TTrack : struct, Enum => Root("Switch", true).Switch<TState, TTrack>(tracks, asking);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Gate{TQuestion}"/>
    protected MonadTask<TInput, TReturn> Gate<TQuestion>(
        Func<Decisions.GateTracks<TInput, TReturn>, Decisions.GateTracks<TInput, TReturn>> gate
    ) => Root("Gate", true).Gate<TQuestion>(gate);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Gate{TState, TQuestion}"/>
    protected MonadTask<TInput, TReturn> Gate<TState, TQuestion>(
        Func<Decisions.GateTracks<TInput, TReturn>, Decisions.GateTracks<TInput, TReturn>> gate,
        string? asking = null
    ) => Root("Gate", true).Gate<TState, TQuestion>(gate, asking);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Scale{TLevel}"/>
    protected MonadTask<TInput, TReturn> Scale<TLevel>(
        Func<
            Decisions.ScaleTracks<TInput, TReturn, TLevel>,
            Decisions.ScaleTracks<TInput, TReturn, TLevel>
        > scale
    )
        where TLevel : struct, Enum => Root("Scale", true).Scale<TLevel>(scale);

    /// <inheritdoc cref="Monad{TInput, TReturn}.Scale{TState, TLevel}"/>
    protected MonadTask<TInput, TReturn> Scale<TState, TLevel>(
        Func<
            Decisions.ScaleTracks<TInput, TReturn, TLevel>,
            Decisions.ScaleTracks<TInput, TReturn, TLevel>
        > scale,
        string? asking = null
    )
        where TLevel : struct, Enum => Root("Scale", true).Scale<TState, TLevel>(scale, asking);

    /// <summary>
    /// Adds a service to the chain's Memory for interface-based junction resolution.
    /// </summary>
    protected Monad<TInput, TReturn> AddServices<T1>(T1 service) =>
        Root("AddServices", false).AddServices(service);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2>(T1 s1, T2 s2) =>
        Root("AddServices", false).AddServices(s1, s2);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3>(T1 s1, T2 s2, T3 s3) =>
        Root("AddServices", false).AddServices(s1, s2, s3);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4>(T1 s1, T2 s2, T3 s3, T4 s4) =>
        Root("AddServices", false).AddServices(s1, s2, s3, s4);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5
    ) => Root("AddServices", false).AddServices(s1, s2, s3, s4, s5);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6
    ) => Root("AddServices", false).AddServices(s1, s2, s3, s4, s5, s6);

    /// <inheritdoc cref="AddServices{T1}"/>
    protected Monad<TInput, TReturn> AddServices<T1, T2, T3, T4, T5, T6, T7>(
        T1 s1,
        T2 s2,
        T3 s3,
        T4 s4,
        T5 s5,
        T6 s6,
        T7 s7
    ) => Root("AddServices", false).AddServices(s1, s2, s3, s4, s5, s6, s7);

    #endregion
}
