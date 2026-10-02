using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>
/// Declares the questions a <c>Decide</c> step asks about one <typeparamref name="TState"/>, all in
/// one call to the decider.
/// </summary>
/// <remarks>
/// Each answer becomes a typed decision in Memory (<see cref="ChoiceDecision{TTrack}"/>,
/// <see cref="ScoreDecision{TLevel}"/> or <see cref="YesNoDecision{TQuestion}"/>) that a later
/// <c>Switch</c>, <c>Scale</c> or <c>Gate</c> routes on, or that a junction reads.
/// </remarks>
public sealed class Questions<TState>
{
    internal Questions() { }

    private readonly List<QuestionSpec> _specs = [];

    private readonly List<Type> _shadows = [];

    private readonly List<string> _problems = [];

    internal IReadOnlyList<QuestionSpec> Specs => _specs;

    internal IReadOnlyList<Type> Shadows => _shadows;

    internal Type Decider { get; private set; } = typeof(IDecider);

    /// <summary>
    /// How long a shadow is waited for once the live answer is in, unless
    /// <see cref="WaitForShadows"/> says otherwise.
    /// </summary>
    internal static readonly TimeSpan DefaultShadowWait = TimeSpan.FromSeconds(5);

    internal TimeSpan ShadowWait { get; private set; } = DefaultShadowWait;

    private bool _deciderNamed;

    /// <summary>What is wrong with the declaration, phrased for whoever has to fix it.</summary>
    internal IEnumerable<string> Problems =>
        _problems
            .Concat(_specs.Select(s => s.Problem).OfType<string>())
            .Concat(_specs.Count == 0 ? ["asks no questions. Add one."] : [])
            .Concat(UncopyableState());

    /// <summary>
    /// A state the shadows cannot each be handed a copy of, when its type alone says so.
    /// </summary>
    private IEnumerable<string> UncopyableState()
    {
        if (_shadows.Count > 0 && StateCopy.ProblemWith(typeof(TState)) is { } problem)
            yield return $"shadows its decisions, and each shadow is handed its own copy of the "
                + $"'{typeof(TState).ReadableName()}' it decides from, written to JSON and read "
                + $"back, but {problem}. Decide from a type JSON can write and read back, or drop "
                + "the shadows.";
    }

    /// <summary>Asks which member of <typeparamref name="TTrack"/> applies.</summary>
    /// <param name="asking">The question. Defaults to the <see cref="AsksAttribute"/> on <typeparamref name="TTrack"/>.</param>
    public Questions<TState> Choice<TTrack>(string? asking = null)
        where TTrack : struct, Enum => Add(new ChoiceSpec<TTrack>(asking));

    /// <summary>
    /// Asks where the state falls on the levels of <typeparamref name="TLevel"/>, lowest value
    /// first.
    /// </summary>
    /// <param name="asking">The question. Defaults to the <see cref="AsksAttribute"/> on <typeparamref name="TLevel"/>.</param>
    public Questions<TState> Score<TLevel>(string? asking = null)
        where TLevel : struct, Enum => Add(new ScoreSpec<TLevel>(asking));

    /// <summary>Asks how likely the answer to <typeparamref name="TQuestion"/> is yes.</summary>
    /// <param name="asking">The question. Defaults to the <see cref="AsksAttribute"/> on <typeparamref name="TQuestion"/>.</param>
    /// <param name="yes">What a yes means. Defaults to the attribute's.</param>
    /// <param name="no">What a no means. Defaults to the attribute's.</param>
    public Questions<TState> YesNo<TQuestion>(
        string? asking = null,
        string? yes = null,
        string? no = null
    ) => Add(new YesNoSpec<TQuestion>(asking, yes, no));

    /// <summary>
    /// Asks <typeparamref name="TDecider"/> instead of the registered <see cref="IDecider"/>.
    /// </summary>
    public Questions<TState> DecidedBy<TDecider>()
        where TDecider : class, IDecider
    {
        if (_deciderNamed)
            _problems.Add("names DecidedBy twice. One decider answers a Decide.");

        _deciderNamed = true;
        Decider = typeof(TDecider);
        return this;
    }

    /// <summary>
    /// Also asks <typeparamref name="TDecider"/> every question the live decider is asked, and
    /// records whether it agrees, without acting on its answers.
    /// </summary>
    /// <remarks>
    /// A shadow that fails, disagrees or is slow never changes the run. It is asked alongside the
    /// live decider, waited for at most <see cref="WaitForShadows"/> once the live answer is in,
    /// and then cancelled and recorded as not having answered. A question whose answer is replayed
    /// is not put to the shadows at all. A shadow that is not registered is a mistake in the host,
    /// so the startup check reports it and the run refuses it, as it does a missing live decider.
    ///
    /// <para>A shadow runs alongside the live decider and, if it ignores cancellation, alongside
    /// the steps after this one, so it shares nothing with the run. Each shadow is handed a copy of
    /// the state of its own: the state is written to JSON once, when the question is asked, and
    /// read back separately for each shadow, so a shadow may change its
    /// <see cref="DecisionRequest.State"/> without the run or another shadow seeing it. A state
    /// type that JSON cannot write and read back is refused by the startup check when that can be
    /// told from the type; otherwise a shadow whose copy cannot be made is recorded as not having
    /// answered, and the run goes on. One from the container is built in a scope of its own,
    /// disposed when it finishes; one that has not finished a second after it was cancelled has
    /// its scope disposed under it, and whatever it then fails with is recorded as its own
    /// failure. One handed to <c>AddServices</c> is the instance the run holds, and is used as it
    /// is.</para>
    /// </remarks>
    public Questions<TState> Shadow<TDecider>()
        where TDecider : class, IDecider => AddShadow(typeof(TDecider));

    /// <summary>
    /// Takes the shadows a <c>Switch</c>, <c>Gate</c> or <c>Scale</c> that asks its own question
    /// declares on its tracks.
    /// </summary>
    internal Questions<TState> ShadowedAs<TInput, TReturn>(TrackSet<TInput, TReturn> tracks)
    {
        foreach (var shadow in tracks.Shadows)
            AddShadow(shadow);

        return tracks.ShadowWait is { } wait ? WaitForShadows(wait) : this;
    }

    private Questions<TState> AddShadow(Type decider)
    {
        if (_shadows.Contains(decider))
            _problems.Add($"shadows with '{decider.ReadableName()}' twice. Name each shadow once.");

        _shadows.Add(decider);
        return this;
    }

    /// <summary>
    /// How long to wait for the shadows once the live answer is in, before cancelling them and
    /// going on without their answers. Defaults to five seconds. Every run waits up to this long
    /// for a slow shadow, so keep it short; zero waits only for shadows that already answered.
    /// </summary>
    public Questions<TState> WaitForShadows(TimeSpan wait)
    {
        if (wait < TimeSpan.Zero || wait.TotalMilliseconds > int.MaxValue)
            _problems.Add(
                $"waits {wait} for its shadows. Wait a bounded, non-negative time, so a shadow "
                    + "that never answers cannot hold up the run."
            );
        else
            ShadowWait = wait;

        return this;
    }

    internal Questions<TState> Add(QuestionSpec spec)
    {
        if (_specs.FirstOrDefault(s => s.Key == spec.Key) is { } earlier)
            _problems.Add(
                earlier.On == spec.On
                    ? $"asks about '{spec.On.ReadableName()}' twice. Ask each question once; its "
                        + "decision stays in Memory for every step after it."
                    : QuestionKey.Shared(spec.Key, earlier.On, spec.On)
            );

        _specs.Add(spec);
        return this;
    }
}
