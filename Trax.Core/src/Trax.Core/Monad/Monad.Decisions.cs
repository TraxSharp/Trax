using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// How many times each question has been asked in this run, so a replay can tell the first
    /// asking of a question from a later one.
    /// </summary>
    private readonly Dictionary<string, int> _askings = [];

    /// <summary>
    /// The type each question key was asked about in this run, so two types asked under one key
    /// are refused here as the startup check refuses them.
    /// </summary>
    private readonly Dictionary<string, Type> _askedAbout = [];

    /// <summary>
    /// How long a shadow that has been cancelled is given to finish before its scope is disposed
    /// under it. The run never waits for this.
    /// </summary>
    private static readonly TimeSpan ShadowGrace = TimeSpan.FromSeconds(1);

    #region Public API

    /// <summary>
    /// Asks a decider every question in <paramref name="questions"/> about the
    /// <typeparamref name="TState"/> in Memory, in one call, and puts each typed decision in
    /// Memory.
    /// </summary>
    /// <remarks>
    /// An answer that does not fit its question (an option that does not exist, a probability
    /// outside 0 to 1, a score off the scale), or no answer at all, fails the run rather than being
    /// acted on, classified <see cref="FailureClass.Transient"/> because asking again may get a
    /// usable one, and the decision observer is told of each such answer, and why, through
    /// <see cref="IDecisionObserver.Refused"/> first; so does a decider that throws, classified as its exception says. A later <see cref="Switch{TTrack}"/>, <see cref="Gate{TQuestion}"/> or
    /// <see cref="Scale{TLevel}"/> routes on the decisions without asking again. An answer replayed
    /// from an earlier run (<see cref="IDecisionReplay"/>) that was given to a different asking of
    /// the question, no longer fits it, or was given about a state other than the one in Memory
    /// now (<see cref="DecisionMade.StateHash"/>), is not acted on either; the decider is asked
    /// afresh instead. A cancelled run asks nothing.
    /// </remarks>
    public MonadTask<TInput, TReturn> Decide<TState>(
        Func<Questions<TState>, Questions<TState>> questions
    )
    {
        var declared = questions(new Questions<TState>());
        var step = $"Decide<{typeof(TState).ReadableName()}>";

        return Recorder is not null
            ? RecordDecide(declared, step)
            : new(DecideAsync(declared, step));
    }

    /// <summary>
    /// Sends the train down the track for the <see cref="ChoiceDecision{TTrack}"/> already in
    /// Memory, decided by an earlier <see cref="Decide{TState}"/>.
    /// </summary>
    public MonadTask<TInput, TReturn> Switch<TTrack>(
        Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks
    )
        where TTrack : struct, Enum
    {
        var declared = tracks(new Tracks<TInput, TReturn, TTrack>());
        var step = $"Switch<{typeof(TTrack).ReadableName()}>";

        return Recorder is not null
            ? RecordRouting<TTrack>(
                ChainStepKind.Switch,
                step,
                typeof(ChoiceDecision<TTrack>),
                declared.Set,
                SwitchProblems(declared).Concat(Unasked(declared.Set))
            )
            : new(SwitchAsync(declared, step, Unasked(declared.Set)));
    }

    /// <summary>
    /// Asks the registered decider which member of <typeparamref name="TTrack"/> applies to the
    /// <typeparamref name="TState"/> in Memory, offering only the members this switch has tracks
    /// for, and sends the train down the chosen track.
    /// </summary>
    /// <param name="tracks">Declares the tracks.</param>
    /// <param name="asking">The question. Defaults to the <see cref="AsksAttribute"/> on <typeparamref name="TTrack"/>.</param>
    public MonadTask<TInput, TReturn> Switch<TState, TTrack>(
        Func<Tracks<TInput, TReturn, TTrack>, Tracks<TInput, TReturn, TTrack>> tracks,
        string? asking = null
    )
        where TTrack : struct, Enum
    {
        var declared = tracks(new Tracks<TInput, TReturn, TTrack>());
        var question = new Questions<TState>()
            .Add(
                new ChoiceSpec<TTrack>(asking, declared.Offered)
                {
                    Route = d => declared.Route((ChoiceDecision<TTrack>)d).Taken?.Name,
                }
            )
            .ShadowedAs(declared.Set);
        var step = $"Switch<{typeof(TState).ReadableName()}, {typeof(TTrack).ReadableName()}>";

        if (Recorder is not null)
        {
            RecordDecide(question, step);
            return RecordRouting<TTrack>(
                ChainStepKind.Switch,
                step,
                typeof(ChoiceDecision<TTrack>),
                declared.Set,
                SwitchProblems(declared)
            );
        }

        return new(
            AskThen(question, step, SwitchProblems(declared), m => m.SwitchAsync(declared, step))
        );
    }

    /// <summary>
    /// Sends the train down the Yes, No or Unsure track for the
    /// <see cref="YesNoDecision{TQuestion}"/> already in Memory.
    /// </summary>
    public MonadTask<TInput, TReturn> Gate<TQuestion>(
        Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate
    )
    {
        var declared = gate(new GateTracks<TInput, TReturn>());
        var step = $"Gate<{typeof(TQuestion).ReadableName()}>";

        return Recorder is not null
            ? RecordRouting<TQuestion>(
                ChainStepKind.Gate,
                step,
                typeof(YesNoDecision<TQuestion>),
                declared.Set,
                declared.Problems.Concat(Unasked(declared.Set))
            )
            : new(GateAsync<TQuestion>(declared, step, Unasked(declared.Set)));
    }

    /// <summary>
    /// Asks the registered decider how likely the answer to <typeparamref name="TQuestion"/> is yes
    /// for the <typeparamref name="TState"/> in Memory, and sends the train down the Yes, No or
    /// Unsure track.
    /// </summary>
    /// <param name="gate">Declares the tracks and the bars between them.</param>
    /// <param name="asking">The question. Defaults to the <see cref="AsksAttribute"/> on <typeparamref name="TQuestion"/>.</param>
    public MonadTask<TInput, TReturn> Gate<TState, TQuestion>(
        Func<GateTracks<TInput, TReturn>, GateTracks<TInput, TReturn>> gate,
        string? asking = null
    )
    {
        var declared = gate(new GateTracks<TInput, TReturn>());
        var question = new Questions<TState>()
            .Add(
                new YesNoSpec<TQuestion>(asking, null, null)
                {
                    Route = d => declared.Route(((YesNoDecision<TQuestion>)d).Probability)?.Name,
                }
            )
            .ShadowedAs(declared.Set);
        var step = $"Gate<{typeof(TState).ReadableName()}, {typeof(TQuestion).ReadableName()}>";

        if (Recorder is not null)
        {
            RecordDecide(question, step);
            return RecordRouting<TQuestion>(
                ChainStepKind.Gate,
                step,
                typeof(YesNoDecision<TQuestion>),
                declared.Set,
                declared.Problems
            );
        }

        return new(
            AskThen(question, step, declared.Problems, m => m.GateAsync<TQuestion>(declared, step))
        );
    }

    /// <summary>
    /// Sends the train down the track for the level the <see cref="ScoreDecision{TLevel}"/> already
    /// in Memory reaches.
    /// </summary>
    public MonadTask<TInput, TReturn> Scale<TLevel>(
        Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale
    )
        where TLevel : struct, Enum
    {
        var declared = scale(new ScaleTracks<TInput, TReturn, TLevel>());
        var step = $"Scale<{typeof(TLevel).ReadableName()}>";

        return Recorder is not null
            ? RecordRouting<TLevel>(
                ChainStepKind.Scale,
                step,
                typeof(ScoreDecision<TLevel>),
                declared.Set,
                declared.Problems.Concat(UnaskedScale(declared))
            )
            : new(ScaleAsync(declared, step, UnaskedScale(declared)));
    }

    /// <summary>
    /// Asks the registered decider where the <typeparamref name="TState"/> in Memory falls on the
    /// levels of <typeparamref name="TLevel"/>, and sends the train down the track for that level.
    /// </summary>
    /// <param name="scale">Declares the tracks.</param>
    /// <param name="asking">The question. Defaults to the <see cref="AsksAttribute"/> on <typeparamref name="TLevel"/>.</param>
    public MonadTask<TInput, TReturn> Scale<TState, TLevel>(
        Func<ScaleTracks<TInput, TReturn, TLevel>, ScaleTracks<TInput, TReturn, TLevel>> scale,
        string? asking = null
    )
        where TLevel : struct, Enum
    {
        var declared = scale(new ScaleTracks<TInput, TReturn, TLevel>());
        var question = new Questions<TState>()
            .Add(
                new ScoreSpec<TLevel>(asking)
                {
                    Route = d => declared.Route((ScoreDecision<TLevel>)d).Taken?.Name,
                }
            )
            .ShadowedAs(declared.Set);
        var step = $"Scale<{typeof(TState).ReadableName()}, {typeof(TLevel).ReadableName()}>";

        if (Recorder is not null)
        {
            RecordDecide(question, step);
            return RecordRouting<TLevel>(
                ChainStepKind.Scale,
                step,
                typeof(ScoreDecision<TLevel>),
                declared.Set,
                declared.Problems
            );
        }

        return new(AskThen(question, step, declared.Problems, m => m.ScaleAsync(declared, step)));
    }

    #endregion

    #region Asking

    /// <summary>
    /// Asks a routing step's own question, then routes on the answer. The tracks are checked
    /// first, so a declaration the startup check refuses never costs a decision.
    /// </summary>
    private async Task<Monad<TInput, TReturn>> AskThen<TState>(
        Questions<TState> question,
        string step,
        IEnumerable<string> trackProblems,
        Func<Monad<TInput, TReturn>, Task<Monad<TInput, TReturn>>> route
    )
    {
        if (Exception is null && trackProblems.ToList() is { Count: > 0 } problems)
            return Refuse(step, $"{step} {string.Join(" ", problems)}");

        var monad = await DecideAsync(question, step).ConfigureAwait(false);
        return await route(monad).ConfigureAwait(false);
    }

    private async Task<Monad<TInput, TReturn>> DecideAsync<TState>(
        Questions<TState> questions,
        string step
    )
    {
        if (Exception is not null)
            return this;

        // A cancelled run neither decides nor tells anyone it did, as a junction does not run.
        CancellationToken.ThrowIfCancellationRequested();

        var train = Train.GetType().ReadableName();
        var at = $"{step} (train '{train}')";

        // A declaration the startup check refuses is refused here too, for a host that skips it.
        if (questions.Problems.ToList() is { Count: > 0 } problems)
            return Refuse(step, $"{step} {string.Join(" ", problems)}");

        foreach (var spec in questions.Specs)
            if (!_askedAbout.TryAdd(spec.Key, spec.On) && _askedAbout[spec.Key] != spec.On)
                return Refuse(
                    step,
                    $"{step} {QuestionKey.Shared(spec.Key, _askedAbout[spec.Key], spec.On)}"
                );

        var state = this.ExtractTypeFromMemory<TState, TInput, TReturn>(missing =>
            $"{at} decides from '{missing.ReadableName()}'"
        );

        if (Exception is not null)
            return this;

        var specs = questions.Specs;
        var nodeIds = specs.ToDictionary(
            s => s.Key,
            s => Nodes.Next(ChainNodeScope.DecideKey(s.DecisionType))
        );
        var answers = new Dictionary<string, Answer>();
        var occurrences = new Dictionary<string, int>();
        var replayed = new System.Collections.Generic.HashSet<string>();
        var replayRefused = new Dictionary<string, string>();
        var questionsAsked = specs.ToDictionary(s => s.Key, s => s.ToQuestion());
        var fingerprints = specs.ToDictionary(
            s => s.Key,
            s => QuestionFingerprint.Of(step, typeof(TState), questionsAsked[s.Key])
        );

        // The state hashed once, on first need and before the decider or any step after this one
        // can change it, so a replayed answer is acted on only for the state it was given about.
        // A state that cannot be hashed has no hash, so nothing recorded for it is replayed; that
        // is never the run's failure.
        string? stateHash = null;
        var hashed = false;
        var keyResolved = true;

        void HashState()
        {
            if (hashed)
                return;

            hashed = true;
            keyResolved = HashKey(out var key);
            stateHash = keyResolved ? StateDigest.Of(state, key) : null;
        }

        try
        {
            var replay = Optional<IDecisionReplay>();

            foreach (var spec in specs)
            {
                var asking = _askings.GetValueOrDefault(spec.Key);
                _askings[spec.Key] = asking + 1;
                occurrences[spec.Key] = asking;

                if (replay is null)
                    continue;

                var earlier = await replay
                    .Replay(train, Train.ExternalId, spec.Key, asking, CancellationToken)
                    .ConfigureAwait(false);

                if (earlier is null)
                    continue;

                // An answer recorded for a different asking (the question reworded, its options
                // changed, another step now asking it first) or against an older declaration (an
                // option since renamed or dropped, fewer levels, another kind of question) cannot
                // repeat what that run did, so the decider is asked as if nothing had been
                // recorded.
                // The reason names what does not fit, never the recorded answer itself.
                var problem =
                    earlier.Fingerprint != fingerprints[spec.Key]
                        ? "it was given to the question as an earlier version of the chain asked "
                            + "it, and the question or the steps asking it have changed since"
                        : spec.ReplayProblem(earlier.Answer);

                if (problem is not null)
                {
                    replayRefused[spec.Key] =
                        $"the answer recorded for '{spec.Key}' no longer fits the question as it "
                        + $"is asked now: {problem}";
                    Warn($"{at}: {replayRefused[spec.Key]}. The decider is asked afresh.");
                    continue;
                }

                // A recorded answer replays only into the state it was given about: the hash of
                // the state asked about now must equal the recorded one exactly.
                HashState();

                if (
                    StateProblem(spec.Key, earlier.StateHash, stateHash, keyResolved) is { } differs
                )
                {
                    replayRefused[spec.Key] = differs;
                    Warn($"{at}: {differs}. The decider is asked afresh.");
                    continue;
                }

                answers[spec.Key] = earlier.Answer;
                replayed.Add(spec.Key);
            }
        }
        catch (Exception e)
        {
            return Failed(e, step);
        }

        // Resolved before the decider is asked, so an observer that cannot be built fails the step
        // before it costs a decision.
        var (observer, unresolved) = ResolveObserver();

        if (unresolved is not null)
            return ObserverFailed(unresolved, step);

        var pending = specs.Where(s => !replayed.Contains(s.Key)).ToList();
        IReadOnlyList<ShadowResult> shadowResults = [];
        Type? deciderType = null;

        if (pending.Count > 0)
        {
            var decider =
                MonadExtensions.ExtractTypeFromMemory(
                    this,
                    questions.Decider,
                    missing => $"{at} needs a decider '{missing.ReadableName()}'"
                ) as IDecider;

            if (decider is null)
                return this;

            deciderType = decider.GetType();

            // Shadows are resolved here, on the run's own thread, so they never read Memory while
            // the run writes to it. One nobody registered is refused, as the startup check
            // refuses it; one that is registered but fails, is slow or disagrees never changes
            // the run. Each one from the container is built in a scope of its own, so it shares
            // no scoped service (a DbContext, say) with the live decider or the junctions after
            // this step, and that scope is disposed when the shadow ends.
            var shadowDeciders = new List<(Type Type, Shadow? Shadow, string? Error)>();

            foreach (var type in questions.Shadows)
            {
                Shadow? resolved;

                try
                {
                    resolved = await ResolveShadow(type).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    shadowDeciders.Add((type, null, $"could not be resolved: {e.Message}"));
                    continue;
                }

                if (resolved is null)
                {
                    foreach (var built in shadowDeciders)
                        if (built.Shadow is { } unused)
                            await unused.DisposeAsync().ConfigureAwait(false);

                    return Refuse(
                        step,
                        $"{at} needs a shadow decider '{type.ReadableName()}' and neither "
                            + "Memory nor the container holds one. Register it, or hand one to "
                            + "AddServices."
                    );
                }

                shadowDeciders.Add((type, resolved, null));
            }

            // Only what the live decider is asked goes to the shadows: a replayed answer is not
            // being decided, so there is nothing to compare.
            var request = new DecisionRequest(
                train,
                state!,
                pending.Select(s => questionsAsked[s.Key]).ToList()
            );

            // Hashed before the live decider is asked, so the hash an observer records is of the
            // state the decider was given.
            if (observer is not null)
                HashState();

            // Written once, here, before the live decider or any step after this one can change
            // the state, and read back separately for each shadow on its own thread, so no shadow
            // shares an object with the run or with another shadow. A state that cannot be
            // written is every shadow's problem, never the run's.
            byte[]? snapshot = null;
            string? uncopied = null;

            if (shadowDeciders.Any(s => s.Shadow is not null))
                try
                {
                    snapshot = StateCopy.Snapshot(state!);
                }
                catch (Exception e)
                {
                    uncopied = Uncopied(e.Message);
                }

            // Disposed when this step is done with the shadows, whether they finished or not, so
            // a shadow that never returns does not keep a registration on the run's token.
            using var shadowing = CancellationTokenSource.CreateLinkedTokenSource(
                CancellationToken
            );
            var shadows = shadowDeciders
                .Select(s =>
                    (
                        Decider: s.Type,
                        Asked: s.Shadow is null
                            ? Task.FromResult(new ShadowResult(s.Type, null, s.Error))
                        : snapshot is null ? Unasked(s.Type, s.Shadow, uncopied!)
                        : AskShadow(
                            s.Type,
                            s.Shadow,
                            request,
                            snapshot,
                            state!.GetType(),
                            shadowing.Token
                        )
                    )
                )
                .ToList();

            try
            {
                var result = await decider.Decide(request, CancellationToken).ConfigureAwait(false);

                foreach (var spec in pending)
                    if (result?.Answers?.GetValueOrDefault(spec.Key) is { } answer)
                        answers[spec.Key] = answer;
            }
            catch (Exception e)
            {
                // The run fails here, so nothing will read what the shadows say.
                Stop(shadowing);
                return Failed(e, step);
            }

            shadowResults = await Settle(shadows, shadowing, questions.ShadowWait)
                .ConfigureAwait(false);
        }

        var decided = new List<(QuestionSpec Spec, Answer Answer, object Decision)>();
        var refused =
            new List<(QuestionSpec Spec, Answer? Answer, string Reason, string Sentence)>();

        foreach (var spec in specs)
        {
            // A decider that skips a question or garbles an answer is a model having a bad call,
            // not a declaration that cannot work, so asking again may well get a usable answer.
            if (!answers.TryGetValue(spec.Key, out var answer))
            {
                var reason = $"gave no answer to '{spec.Key}'";
                refused.Add((spec, null, reason, $"{reason}, so there is nothing to act on."));
                continue;
            }

            try
            {
                decided.Add((spec, answer, spec.ToDecision(answer)));
            }
            catch (InvalidAnswerException invalid)
            {
                // A replayed answer was checked before it was kept, so this one is the decider's.
                refused.Add(
                    (spec, answer, invalid.Message, $"{invalid.Message}, so it is not acted on.")
                );
            }
        }

        if (refused.Count > 0)
        {
            // Told before the step fails, so the answer the run would not act on, and why, is on
            // record next to the failure rather than lost with it.
            if (observer is not null)
                foreach (var (spec, answer, reason, _) in refused)
                {
                    var refusal = new DecisionRefused(
                        train,
                        Train.ExternalId,
                        questionsAsked[spec.Key],
                        occurrences[spec.Key],
                        fingerprints[spec.Key],
                        answer,
                        deciderType!,
                        $"the decider {reason}"
                    )
                    {
                        QuestionType = spec.On,
                    };

                    ChainGraph.Enter(nodeIds[spec.Key], BranchPath);

                    // The step fails on the refusal either way, so an observer that cannot record
                    // it is only logged: its failure would hide why the step failed.
                    if (
                        await Tell(observer, (o, ct) => o.Refused(refusal, ct), at)
                            .ConfigureAwait(false) is
                        { } failed
                    )
                        Warn(
                            $"{at}: the decision observer '{observer.GetType().ReadableName()}' "
                                + $"could not record the refused answer to '{spec.Key}': "
                                + failed.Message
                        );
                }

            return Refuse(
                step,
                $"{at}: "
                    + string.Join(
                        " ",
                        refused.Select(
                            (r, i) => (i == 0 ? "the decider " : "The decider ") + r.Sentence
                        )
                    ),
                FailureClass.Transient
            );
        }

        CancellationToken.ThrowIfCancellationRequested();

        foreach (var (spec, answer, decision) in decided)
        {
            Memory[spec.DecisionType] = decision;

            var wasReplayed = replayed.Contains(spec.Key);
            var compared = wasReplayed
                ? []
                : shadowResults
                    .Select(s =>
                        s.Answers?.GetValueOrDefault(spec.Key) is { } shadow
                            ? new ShadowAnswer(
                                s.Decider,
                                shadow,
                                spec.SameOutcome(answer, shadow),
                                null
                            )
                            : new ShadowAnswer(
                                s.Decider,
                                null,
                                false,
                                s.Error ?? $"gave no answer to '{spec.Key}'"
                            )
                    )
                    .ToList();

            if (observer is null)
                continue;

            var made = new DecisionMade(
                train,
                Train.ExternalId,
                questionsAsked[spec.Key],
                occurrences[spec.Key],
                fingerprints[spec.Key],
                answer,
                wasReplayed ? null : deciderType,
                wasReplayed,
                compared,
                replayRefused.GetValueOrDefault(spec.Key)
            )
            {
                StateHash = stateHash,
                QuestionType = spec.On,
                StateType = (object?)state is { } held ? held.GetType() : typeof(TState),
            };

            ChainGraph.Enter(nodeIds[spec.Key], BranchPath);

            if (
                await Tell(observer, (o, ct) => o.Decided(made, ct), at).ConfigureAwait(false) is
                { } failed
            )
                return ObserverFailed(failed, step);
        }

        return this;
    }

    private sealed record ShadowResult(
        Type Decider,
        IReadOnlyDictionary<string, Answer>? Answers,
        string? Error
    );

    /// <summary>
    /// A shadow decider, and the scope it was built in when it came from the container. The scope
    /// is disposed once, by whichever comes first: the shadow ending, or the grace period after it
    /// was cancelled running out.
    /// </summary>
    private sealed class Shadow(IDecider decider, AsyncServiceScope? scope) : IAsyncDisposable
    {
        private int _disposed;

        public IDecider Decider { get; } = decider;

        public async ValueTask DisposeAsync()
        {
            if (scope is { } built && Interlocked.Exchange(ref _disposed, 1) == 0)
                await built.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the scope once <paramref name="grace"/> has passed, on a timer of its own, so
        /// a shadow that ignores cancellation and never returns does not keep its scope, and the
        /// caller does not wait for either.
        /// </summary>
        public async Task DisposeAfter(TimeSpan grace)
        {
            try
            {
                await Task.Delay(grace).ConfigureAwait(false);
                await DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // A shadow's scope failing to dispose is the shadow's problem, not the run's.
            }
        }
    }

    /// <summary>
    /// The shadow decider <paramref name="type"/>, or null when nothing supplies one. One handed to
    /// AddServices is used as it is. One from the container is built in a new scope when the
    /// container can make one, so the run's scoped services are never shared with it.
    /// </summary>
    private async Task<Shadow?> ResolveShadow(Type type)
    {
        if (Memory.GetValueOrDefault(type) is { } held)
            return held is IDecider given ? new Shadow(given, null) : null;

        if (Memory.GetValueOrDefault(typeof(IServiceProvider)) is not IServiceProvider container)
            return null;

        if (container.GetService(typeof(IServiceScopeFactory)) is not IServiceScopeFactory scopes)
            return container.GetService(type) is IDecider unscoped
                ? new Shadow(unscoped, null)
                : null;

        var scope = scopes.CreateAsyncScope();

        try
        {
            if (scope.ServiceProvider.GetService(type) is IDecider scoped)
                return new Shadow(scoped, scope);
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await scope.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// The <see cref="StateHashKey"/> from the train's container, or null when it registers none.
    /// False when resolving it failed, so the state goes unhashed and nothing is replayed, rather
    /// than hashed without the key the host meant to use.
    /// </summary>
    private bool HashKey(out StateHashKey? key)
    {
        key = null;

        try
        {
            key =
                (
                    Memory.GetValueOrDefault(typeof(IServiceProvider)) as IServiceProvider
                )?.GetService(typeof(StateHashKey)) as StateHashKey;
            return true;
        }
        catch (Exception e)
        {
            Warn(
                $"the state hash key could not be resolved, so the state is not hashed: {e.Message}"
            );
            return false;
        }
    }

    private static string Uncopied(string why) =>
        $"was not asked, because its own copy of the state could not be made through JSON: {why}";

    /// <summary>
    /// Why an answer recorded under <paramref name="recorded"/> is not about the state as it is now,
    /// hashed as <paramref name="current"/>, or null when it is. Refuses unless both hashes exist
    /// and are equal, so an answer recorded before states were hashed, or a state that cannot be
    /// written, is asked afresh rather than assumed to match. The reason says when the state hash
    /// key is what differs, or could not be resolved, rather than blaming the state.
    /// </summary>
    private static string? StateProblem(
        string key,
        string? recorded,
        string? current,
        bool keyResolved
    )
    {
        var answer = $"the answer recorded for '{key}'";

        if (recorded is null)
            return $"{answer} was recorded without a hash of the state it was given about, so it "
                + "cannot be shown to be about the state as it is now";

        if (current is null)
            return keyResolved
                ? $"{answer} cannot be shown to be about the state as it is now, because the "
                    + "state cannot be hashed to compare it: it holds a cycle, a delegate, a "
                    + "handle or something else that cannot be read the same way every time, or "
                    + "is too large or too deep"
                : $"{answer} cannot be shown to be about the state as it is now, because the "
                    + "state hash key could not be resolved, so the state was not hashed";

        if (string.Equals(recorded, current, StringComparison.Ordinal))
            return null;

        var recordedKeyed = recorded.StartsWith(StateDigest.KeyedPrefix, StringComparison.Ordinal);
        var currentKeyed = current.StartsWith(StateDigest.KeyedPrefix, StringComparison.Ordinal);

        return recordedKeyed && !currentKeyed
                ? $"{answer} was recorded under a state hash key and this run hashes the state "
                    + "without one, so it cannot be compared"
            : !recordedKeyed && currentKeyed
                ? $"{answer} was recorded without a state hash key and this run hashes the state "
                    + "under one, so it cannot be compared"
            : currentKeyed
                ? $"{answer} was given about a different state from the one it is asked about "
                    + "now, or the state hash key has changed since"
            : $"{answer} was given about a different state from the one it is asked about now";
    }

    /// <summary>
    /// A shadow that cannot be asked, because the state could not be written for it, recorded as
    /// not having answered. Its scope is released at once.
    /// </summary>
    private static async Task<ShadowResult> Unasked(Type deciderType, Shadow shadow, string why)
    {
        try
        {
            await shadow.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The shadow's problem, not the run's.
        }

        return new ShadowResult(deciderType, null, why);
    }

    /// <summary>
    /// Asks a shadow decider on a thread of its own, turning every way it can fail into a result,
    /// so the run never depends on it, not even on a shadow that blocks before it returns a task.
    /// The shadow is handed a copy of the state of its own, read back from
    /// <paramref name="snapshot"/>. Its scope is disposed when it ends, or, for one that is still
    /// running once it has been cancelled, after <see cref="ShadowGrace"/>.
    /// </summary>
    private static Task<ShadowResult> AskShadow(
        Type deciderType,
        Shadow shadow,
        DecisionRequest request,
        byte[] snapshot,
        Type stateType,
        CancellationToken cancellationToken
    ) =>
        Task.Run(
            async () =>
            {
                // Registered before anything else, so a shadow cancelled before its thread ran
                // still has its scope released. The callback only starts a timer.
                var grace = cancellationToken.Register(() => _ = shadow.DisposeAfter(ShadowGrace));

                try
                {
                    object copy;

                    try
                    {
                        copy = StateCopy.Read(snapshot, stateType);
                    }
                    catch (Exception e)
                    {
                        return new ShadowResult(deciderType, null, Uncopied(e.Message));
                    }

                    var result = await shadow
                        .Decider.Decide(request with { State = copy }, cancellationToken)
                        .ConfigureAwait(false);
                    return new ShadowResult(deciderType, result?.Answers, null);
                }
                catch (Exception e)
                {
                    // Including an ObjectDisposedException from a scope disposed under a shadow
                    // that outlived its grace period: by then nothing reads this result.
                    return new ShadowResult(deciderType, null, $"failed: {e.Message}");
                }
                finally
                {
                    await grace.DisposeAsync().ConfigureAwait(false);

                    try
                    {
                        await shadow.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // A shadow's scope failing to dispose is the shadow's problem, not the
                        // run's, which may already have moved on.
                    }
                }
            },
            CancellationToken.None
        );

    /// <summary>
    /// Waits at most <paramref name="wait"/> for the shadows still running once the live answer is
    /// in, then cancels them and reports each one that has not answered as not having answered.
    /// A shadow is never waited for beyond that, whether or not it honours cancellation.
    /// </summary>
    private async Task<IReadOnlyList<ShadowResult>> Settle(
        List<(Type Decider, Task<ShadowResult> Asked)> shadows,
        CancellationTokenSource shadowing,
        TimeSpan wait
    )
    {
        if (shadows.Count == 0)
            return [];

        var all = Task.WhenAll(shadows.Select(s => s.Asked));

        if (!all.IsCompleted)
        {
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            timer.CancelAfter(wait);

            var gave = new TaskCompletionSource();

            await using (timer.Token.Register(() => gave.TrySetResult()).ConfigureAwait(false))
                await Task.WhenAny(all, gave.Task).ConfigureAwait(false);
        }

        Stop(shadowing);

        return shadows
            .Select(s =>
                s.Asked.IsCompletedSuccessfully
                    ? s.Asked.Result
                    : new ShadowResult(
                        s.Decider,
                        null,
                        $"did not answer within {QuestionSpec.Format(wait.TotalSeconds)}s of the live "
                            + "decider's answer, "
                            + "so it was cancelled and not waited for"
                    )
            )
            .ToList();
    }

    private static void Stop(CancellationTokenSource shadowing)
    {
        try
        {
            shadowing.Cancel();
        }
        catch (AggregateException)
        {
            // A shadow's own cancellation callback threw. It is the shadow's problem, not the run's.
        }
    }

    #endregion

    #region Routing

    /// <summary>
    /// What is wrong with a routing step that asks nothing, beyond its tracks: shadows have
    /// nothing to be compared on.
    /// </summary>
    private static IEnumerable<string> Unasked(TrackSet<TInput, TReturn> set) =>
        set.ShadowsWithoutAQuestion is { } problem ? [problem] : [];

    /// <summary>
    /// What is wrong with a <c>Scale</c> that asks nothing: as for any routing step, and a scale
    /// too short to have been scored, which no question of its own reports.
    /// </summary>
    private static IEnumerable<string> UnaskedScale<TLevel>(
        ScaleTracks<TInput, TReturn, TLevel> scale
    )
        where TLevel : struct, Enum =>
        Unasked(scale.Set)
            .Concat(
                ScaleTracks<TInput, TReturn, TLevel>.TooFewLevels is { } problem ? [problem] : []
            );

    private static IEnumerable<string> SwitchProblems<TTrack>(
        Tracks<TInput, TReturn, TTrack> tracks
    )
        where TTrack : struct, Enum =>
        tracks.Set.Tracks.Count == 0
            ? tracks.Set.Problems.Append("declares no tracks. Add one with When.")
            : tracks.Set.Problems;

    private async Task<Monad<TInput, TReturn>> SwitchAsync<TTrack>(
        Tracks<TInput, TReturn, TTrack> tracks,
        string step,
        IEnumerable<string>? unasked = null
    )
        where TTrack : struct, Enum
    {
        if (Exception is not null)
            return this;

        CancellationToken.ThrowIfCancellationRequested();

        if (SwitchProblems(tracks).Concat(unasked ?? []).ToList() is { Count: > 0 } problems)
            return Refuse(step, $"{step} {string.Join(" ", problems)}");

        var decision = Decision<ChoiceDecision<TTrack>>(step);

        if (decision is null)
            return this;

        var (taken, reason) = tracks.Route(decision);

        return await Take<TTrack>(
                ChainStepKind.Switch,
                step,
                taken,
                reason,
                $"{reason}, and it declares no Otherwise track to take instead."
            )
            .ConfigureAwait(false);
    }

    private async Task<Monad<TInput, TReturn>> GateAsync<TQuestion>(
        GateTracks<TInput, TReturn> gate,
        string step,
        IEnumerable<string>? unasked = null
    )
    {
        if (Exception is not null)
            return this;

        CancellationToken.ThrowIfCancellationRequested();

        if (gate.Problems.Concat(unasked ?? []).ToList() is { Count: > 0 } problems)
            return Refuse(step, $"{step} {string.Join(" ", problems)}");

        var decision = Decision<YesNoDecision<TQuestion>>(step);

        if (decision is null)
            return this;

        var p = decision.Probability;

        return await Take<TQuestion>(
                ChainStepKind.Gate,
                step,
                gate.Route(p),
                null,
                $"the probability of yes was {QuestionSpec.Format(p)}, between the No bar "
                    + $"({QuestionSpec.Format(gate.NoBelow)}) and the Yes bar "
                    + $"({QuestionSpec.Format(gate.YesAtLeast)}), and it declares no Unsure track."
            )
            .ConfigureAwait(false);
    }

    private async Task<Monad<TInput, TReturn>> ScaleAsync<TLevel>(
        ScaleTracks<TInput, TReturn, TLevel> scale,
        string step,
        IEnumerable<string>? unasked = null
    )
        where TLevel : struct, Enum
    {
        if (Exception is not null)
            return this;

        CancellationToken.ThrowIfCancellationRequested();

        if (scale.Problems.Concat(unasked ?? []).ToList() is { Count: > 0 } problems)
            return Refuse(step, $"{step} {string.Join(" ", problems)}");

        var decision = Decision<ScoreDecision<TLevel>>(step);

        if (decision is null)
            return this;

        var (taken, reason) = scale.Route(decision);

        return await Take<TLevel>(
                ChainStepKind.Scale,
                step,
                taken,
                reason,
                $"{reason}, and it declares no Otherwise track to take instead."
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records which track is taken and runs it, or fails the run when there is none to take.
    /// </summary>
    private async Task<Monad<TInput, TReturn>> Take<TKey>(
        ChainStepKind kind,
        string step,
        DeclaredTrack<TInput, TReturn>? taken,
        string? fallbackReason,
        string noTrack
    )
    {
        var train = Train.GetType().ReadableName();
        var at = $"{step} (train '{train}')";

        var nodeId = Nodes.Next(ChainNodeScope.RoutingKey(kind, typeof(TKey)));
        ChainGraph.Enter(nodeId, BranchPath);

        if (taken is null)
            return Refuse(step, $"{at}: {noTrack}");

        Memory[typeof(TrackTaken<TKey>)] = new TrackTaken<TKey>(taken.Name, fallbackReason);

        var (observer, unresolved) = ResolveObserver();

        if (unresolved is not null)
            return ObserverFailed(unresolved, step);

        if (observer is not null)
        {
            var routed = new TrackRouted(
                train,
                Train.ExternalId,
                typeof(TKey),
                taken.Name,
                fallbackReason
            );

            if (
                await Tell(observer, (o, ct) => o.Routed(routed, ct), at).ConfigureAwait(false) is
                { } failed
            )
                return ObserverFailed(failed, step);
        }

        // An observer may have taken a while; a run cancelled meanwhile does not enter the track.
        CancellationToken.ThrowIfCancellationRequested();

        // The track's steps are numbered within the track, as the graph draws them.
        var outer = Nodes;
        Nodes = outer.Track(nodeId, taken.Name);

        try
        {
            return await taken
                .Body(new MonadTask<TInput, TReturn>(Task.FromResult(this)))
                .ConfigureAwait(false);
        }
        finally
        {
            Nodes = outer;
        }
    }

    private TDecision? Decision<TDecision>(string step)
        where TDecision : class =>
        this.ExtractTypeFromMemory<TDecision, TInput, TReturn>(missing =>
            $"{step} (train '{Train.GetType().ReadableName()}') routes on "
            + $"'{missing.ReadableName()}', which nothing decided"
        );

    #endregion

    #region Recording

    private MonadTask<TInput, TReturn> RecordDecide<TState>(
        Questions<TState> questions,
        string step
    )
    {
        var recorder = Recorder!;

        foreach (var problem in questions.Problems)
            recorder.RefuseStep(ChainStepKind.Decide, questions.Decider, $"{step} {problem}");

        // Two types asked under one key anywhere in the chain, tracks included, would have their
        // answers recorded and replayed as if they were one question's.
        foreach (var spec in questions.Specs)
            if (recorder.ClaimQuestionKey(spec.Key, spec.On) is { } other)
                recorder.RefuseStep(
                    ChainStepKind.Decide,
                    questions.Decider,
                    $"{step} {QuestionKey.Shared(spec.Key, other, spec.On)}"
                );

        var first = recorder.Steps.Count;

        if (questions.Specs.Count == 0)
            recorder.Record(ChainStepKind.Decide, questions.Decider, typeof(TState), null);

        foreach (var spec in questions.Specs)
            recorder.Record(
                ChainStepKind.Decide,
                questions.Decider,
                typeof(TState),
                spec.DecisionType
            );

        foreach (var shadow in questions.Shadows)
            recorder.RecordRequirement(first, shadow);

        Vet(questions, step, first);

        return new MonadTask<TInput, TReturn>(Task.FromResult(this));
    }

    /// <summary>
    /// Asks each decider the step names, the live one and its shadows, that can be found without
    /// running anything and that vets questions (<see cref="IVetsQuestions"/>), whether it can
    /// answer what the step declares, and refuses the step for each problem it names.
    /// </summary>
    /// <remarks>
    /// A decider is found as the run finds it, among the services handed to <c>AddServices</c> and
    /// then in the train's container, and is never asked to decide. One the container cannot build
    /// here, because only a request can, say, is left to refuse at run time what it cannot answer;
    /// one nothing supplies is reported by verification.
    /// </remarks>
    private void Vet<TState>(Questions<TState> questions, string step, int first)
    {
        DeclaredQuestions declared;

        try
        {
            declared = new DeclaredQuestions(
                Train.GetType().ReadableName(),
                step,
                typeof(TState),
                questions.Specs.Select(s => s.ToQuestion()).ToList()
            );
        }
        catch
        {
            // A question that cannot be put together is already refused for what is wrong with it.
            return;
        }

        if (declared.Questions.Count == 0)
            return;

        var deciders = questions
            .Shadows.Select(s => (Type: s, Role: "shadow decider"))
            .Prepend((Type: questions.Decider, Role: "decider"));

        foreach (var (type, role) in deciders)
        {
            IVetsQuestions? vetter;

            try
            {
                vetter =
                    (
                        Memory.GetValueOrDefault(type)
                        ?? (
                            Memory.GetValueOrDefault(typeof(IServiceProvider)) as IServiceProvider
                        )?.GetService(type)
                    ) as IVetsQuestions;
            }
            catch
            {
                continue;
            }

            if (vetter is null)
                continue;

            List<string> problems;

            try
            {
                problems = (vetter.Problems(declared) ?? [])
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();
            }
            catch (Exception e)
            {
                problems = [$"failed while vetting the questions: {e.Message}"];
            }

            foreach (var problem in problems)
                Recorder!.RefuseRecordedStep(
                    first,
                    ChainStepKind.Decide,
                    type,
                    $"{step}: the {role} '{vetter.GetType().ReadableName()}' cannot answer it: "
                        + problem
                );
        }
    }

    private MonadTask<TInput, TReturn> RecordRouting<TKey>(
        ChainStepKind kind,
        string step,
        Type decision,
        TrackSet<TInput, TReturn> set,
        IEnumerable<string> problems
    )
    {
        var recorder = Recorder!;

        foreach (var problem in problems)
            recorder.RefuseStep(kind, null, $"{step} {problem}");

        var index = recorder.Steps.Count;
        recorder.Record(kind, null, decision, typeof(TrackTaken<TKey>));

        var recorded = new List<ChainTrack>();

        foreach (var track in set.All)
        {
            var steps = RecordTrack(track);

            // A track's refusals are the chain's refusals, carried at the routing step.
            foreach (var refusal in steps.RecordedRefusals)
                recorder.RefuseRecordedStep(
                    index,
                    kind,
                    refusal.Junction,
                    $"{step}, track '{track.Name}': {refusal.Reason}"
                );

            recorded.Add(
                new ChainTrack(track.Name, track.Description, track == set.Fallback, steps)
            );
        }

        recorder.RecordTracks(index, recorded);

        return new MonadTask<TInput, TReturn>(Task.FromResult(this));
    }

    /// <summary>
    /// Records one track's body into a recorder of its own. Recording is synchronous and runs
    /// nothing.
    /// </summary>
    private ChainRecorder RecordTrack(DeclaredTrack<TInput, TReturn> track)
    {
        var outer = Recorder;
        var steps = new ChainRecorder(outer!);
        Recorder = steps;

        try
        {
            _ = track.Body(new MonadTask<TInput, TReturn>(Task.FromResult(this)));
        }
        finally
        {
            Recorder = outer;
        }

        return steps;
    }

    #endregion

    #region Helpers

    /// <summary>A service that may be absent: in Memory, then the container, else null.</summary>
    private T? Optional<T>()
        where T : class => Optional(typeof(T)) as T;

    private object? Optional(Type type) =>
        Memory.GetValueOrDefault(type) ?? this.ExtractTypeFromServiceProvider(type);

    /// <summary>
    /// The decision observer, or null when none is registered, or what resolving it threw.
    /// </summary>
    private (IDecisionObserver? Observer, Exception? Failure) ResolveObserver()
    {
        try
        {
            return (Optional<IDecisionObserver>(), null);
        }
        catch (Exception e)
        {
            return (null, e);
        }
    }

    /// <summary>
    /// Tells the observer, and returns what it threw when that must fail the step: only when the
    /// observer is <see cref="IDecisionObserver.Required"/>. Anything else it throws is logged and
    /// ignored, because recording a decision must never change it.
    /// </summary>
    private async Task<Exception?> Tell(
        IDecisionObserver observer,
        Func<IDecisionObserver, CancellationToken, Task> tell,
        string at
    )
    {
        try
        {
            await tell(observer, CancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception e)
        {
            bool required;

            try
            {
                required = observer.Required;
            }
            catch
            {
                // An observer that cannot say whether it is required is treated as one that is.
                required = true;
            }

            if (required)
                return e;

            Warn(
                $"{at}: the decision observer '{observer.GetType().ReadableName()}' failed and is "
                    + $"not required, so the run goes on without its record: {e.Message}"
            );
            return null;
        }
    }

    /// <summary>
    /// Logs a problem the run carries on past, through the logger factory in Memory or the
    /// container when there is one.
    /// </summary>
    private void Warn(string message)
    {
        try
        {
            if (Optional<ILoggerFactory>()?.CreateLogger("Trax.Core.Decisions") is { } logger)
                LogProblem(logger, message, null);
        }
        catch
        {
            // Logging a problem must not become one.
        }
    }

    private static readonly Action<ILogger, string, Exception?> LogProblem =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "DecisionProblem"),
            "{Problem}"
        );

    /// <summary>
    /// Fails the run for a decision Trax will not act on. By default the failure is classified
    /// permanent: a declaration that cannot work, or a decision with no track to take, gets the
    /// same refusal however often it is run. A decider's missing or unfit answer passes
    /// <see cref="FailureClass.Transient"/>, because asking again may get a usable one.
    /// </summary>
    private Monad<TInput, TReturn> Refuse(
        string step,
        string reason,
        FailureClass failure = FailureClass.Permanent
    )
    {
        var refusal = new TrainException(reason);
        refusal.Data["TrainExceptionData"] = ExceptionData(refusal, step, failure);
        Exception ??= refusal;
        return this;
    }

    /// <summary>
    /// Fails the run with what a decider or replay threw, carrying the step the way a junction's
    /// failure carries its junction. Cancellation passes through unwrapped, as it does there.
    /// </summary>
    /// <param name="e">What was thrown.</param>
    /// <param name="step">The step it failed.</param>
    /// <param name="unclassified">The class to record when <paramref name="e"/> carries none.</param>
    private Monad<TInput, TReturn> Failed(
        Exception e,
        string step,
        FailureClass? unclassified = null
    )
    {
        if (!(e is OperationCanceledException && CancellationToken.IsCancellationRequested))
            e.Data["TrainExceptionData"] = ExceptionData(
                e,
                step,
                FailureClassification.Carried(e) ?? unclassified
            );

        Exception ??= e;
        return this;
    }

    /// <summary>
    /// Fails the step because a required observer could not record it, or no observer could be
    /// resolved. Recording again may well work, so unless the exception says otherwise the
    /// failure is classified transient.
    /// </summary>
    private Monad<TInput, TReturn> ObserverFailed(Exception e, string step) =>
        Failed(e, step, FailureClass.Transient);

    private TrainExceptionData ExceptionData(Exception e, string step, FailureClass? failure) =>
        new()
        {
            TrainName = Train.GetType().Name,
            TrainExternalId = Train.ExternalId,
            Junction = step,
            Type = e.GetType().Name,
            Message = e.Message,
            StackTrace = e.StackTrace,
            FailureClass = failure,
        };

    #endregion
}
