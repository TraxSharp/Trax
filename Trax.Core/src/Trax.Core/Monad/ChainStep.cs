namespace Trax.Core.Monad;

/// <summary>
/// The kind of step a train's junction chain declares.
/// </summary>
public enum ChainStepKind
{
    /// <summary>A junction resolved by its concrete type.</summary>
    Chain,

    /// <summary>A junction resolved from Memory by its interface type.</summary>
    IChain,

    /// <summary>A junction that may end the chain early with the train's return value.</summary>
    ShortCircuit,

    /// <summary>A projection from one type in Memory to another.</summary>
    Extract,

    /// <summary>The terminal step that produces the train's result.</summary>
    Resolve,

    /// <summary>
    /// A value handed to the chain directly, by <c>AddServices</c> or by <c>Extract</c> given a
    /// value, which puts its type into Memory without a junction producing it.
    /// </summary>
    Seed,

    /// <summary>
    /// Asks a decider a question about a value in Memory and puts the typed decision in Memory.
    /// The step's junction is the decider, its input the state and its output the decision. A
    /// <c>Decide</c> asking several questions records one step per question.
    /// </summary>
    Decide,

    /// <summary>
    /// Routes on a choice in Memory to one of several declared tracks, which
    /// <see cref="ChainRecorder.TracksAt(int)"/> holds.
    /// </summary>
    Switch,

    /// <summary>Routes on a yes/no probability in Memory to its Yes, No or Unsure track.</summary>
    Gate,

    /// <summary>Routes on a score in Memory to the track for the level it reaches.</summary>
    Scale,
}

/// <summary>
/// One track a routing step declares, with the steps it runs recorded on their own.
/// </summary>
/// <param name="Name">The track's name: an enum member, <c>Yes</c>, <c>No</c>, <c>Unsure</c> or <c>Otherwise</c>.</param>
/// <param name="Description">What the track is for, as offered to the decider.</param>
/// <param name="IsFallback">True for the <c>Otherwise</c> or <c>Unsure</c> track.</param>
/// <param name="Steps">The track's chain, recorded the way a train's chain is.</param>
public sealed record ChainTrack(
    string Name,
    string? Description,
    bool IsFallback,
    ChainRecorder Steps
);

/// <summary>
/// One declared step of a train's junction chain, carrying only types. A step is recorded rather than
/// executed, so a chain can be read at startup without running any of the work it describes.
/// </summary>
/// <param name="Kind">Which chain primitive declared this step.</param>
/// <param name="Junction">The junction type, or null for steps that name no junction.</param>
/// <param name="In">The type the step consumes from Memory, or null when it consumes nothing.</param>
/// <param name="Out">The type the step contributes to Memory, or null when it contributes nothing.</param>
public readonly record struct ChainStep(ChainStepKind Kind, Type? Junction, Type? In, Type? Out);

/// <summary>
/// Collects the steps a train declares while its junction chain is being read.
/// </summary>
/// <remarks>
/// A monad carrying a recorder answers every chain call by writing the call's type arguments
/// here and returning immediately. No junction runs and no junction is resolved, so reading a
/// chain is safe to do at host startup for every registered train. The one thing looked up is each
/// decider a decision step names, so one that vets questions
/// (<see cref="Trax.Core.Decisions.IVetsQuestions"/>) can refuse what it cannot answer; none is
/// asked to decide.
/// </remarks>
public sealed class ChainRecorder
{
    internal ChainRecorder() { }

    /// <summary>
    /// A recorder for one track of a routing step, sharing the question keys of the chain it is
    /// part of.
    /// </summary>
    internal ChainRecorder(ChainRecorder chain) => _questionKeys = chain._questionKeys;

    /// <summary>
    /// The type each question key was first asked about, across the whole chain and its tracks.
    /// </summary>
    private readonly Dictionary<string, Type> _questionKeys = [];

    /// <summary>
    /// Notes that the chain asks about <paramref name="about"/> under <paramref name="key"/>, and
    /// returns the other type the chain already asked about under that key, or null.
    /// </summary>
    internal Type? ClaimQuestionKey(string key, Type about)
    {
        if (_questionKeys.TryAdd(key, about))
            return null;

        return _questionKeys[key] == about ? null : _questionKeys[key];
    }

    private readonly List<ChainStep> _steps = [];

    private readonly List<string> _refusals = [];

    private readonly List<RecordedRefusal> _recordedRefusals = [];

    private readonly System.Collections.Generic.HashSet<int> _builtSteps = [];

    private readonly Dictionary<int, IReadOnlyList<ChainTrack>> _tracks = [];

    private readonly Dictionary<int, List<Type>> _requirements = [];

    /// <summary>
    /// The tracks of the routing step at <paramref name="stepIndex"/>, or none for any other step.
    /// </summary>
    public IReadOnlyList<ChainTrack> TracksAt(int stepIndex) =>
        _tracks.TryGetValue(stepIndex, out var tracks) ? tracks : [];

    internal void RecordTracks(int stepIndex, IReadOnlyList<ChainTrack> tracks) =>
        _tracks[stepIndex] = tracks;

    /// <summary>
    /// Notes a service the step at <paramref name="stepIndex"/> resolves besides its junction, such
    /// as a shadow decider, so verification can check it is supplied.
    /// </summary>
    internal void RecordRequirement(int stepIndex, Type service)
    {
        if (!_requirements.TryGetValue(stepIndex, out var services))
            _requirements[stepIndex] = services = [];

        services.Add(service);
    }

    internal IReadOnlyList<Type> RequirementsAt(int stepIndex) =>
        _requirements.TryGetValue(stepIndex, out var services) ? services : [];

    /// <summary>The steps declared, in the order the chain declares them.</summary>
    /// <remarks>
    /// A step naming a type that is not a junction is still listed, with no input or output, so a
    /// step's index is its written position whether or not an earlier step was refused.
    /// </remarks>
    public IReadOnlyList<ChainStep> Steps => _steps;

    /// <summary>
    /// Things the declaration did that no step can express, such as stating its result directly
    /// or awaiting work, each phrased for whoever has to fix it.
    /// </summary>
    /// <remarks>
    /// <see cref="ChainVerification.Verify(ChainRecorder, Type, Type, Func{Type, bool}?)"/> reports
    /// each of these as a <see cref="ChainFault"/> with <see cref="ChainFault.IsRefusal"/> set,
    /// carrying the step it belongs to.
    /// </remarks>
    public IReadOnlyList<string> Refusals => _refusals;

    internal IReadOnlyList<RecordedRefusal> RecordedRefusals => _recordedRefusals;

    internal void Record(ChainStepKind kind, Type? junction, Type? tIn, Type? tOut) =>
        _steps.Add(new ChainStep(kind, junction, tIn, tOut));

    /// <summary>
    /// Records a step whose junction Trax builds from its constructor, so verification can check
    /// that constructor's arguments when asked to.
    /// </summary>
    internal void RecordBuilt(ChainStepKind kind, Type junction, Type tIn, Type tOut)
    {
        _builtSteps.Add(_steps.Count);
        Record(kind, junction, tIn, tOut);
    }

    internal bool IsBuilt(int stepIndex) => _builtSteps.Contains(stepIndex);

    /// <summary>
    /// Refuses the chain as a whole, for something no single step did.
    /// </summary>
    internal void Refuse(string reason) => Add(new RecordedRefusal(null, null, null, reason));

    /// <summary>
    /// Refuses the step about to be recorded. The caller records that step next, so the refusal
    /// carries the step's written position.
    /// </summary>
    internal void RefuseStep(ChainStepKind kind, Type? junction, string reason) =>
        Add(new RecordedRefusal(_steps.Count, kind, junction, reason));

    /// <summary>
    /// Refuses a step already recorded, for a refusal found inside one of a routing step's tracks.
    /// </summary>
    internal void RefuseRecordedStep(
        int stepIndex,
        ChainStepKind kind,
        Type? junction,
        string reason
    ) => Add(new RecordedRefusal(stepIndex, kind, junction, reason));

    private void Add(RecordedRefusal refusal)
    {
        _refusals.Add(refusal.Reason);
        _recordedRefusals.Add(refusal);
    }

    private string? _firstAsyncRootCall;

    private bool _unlinkedRefused;

    /// <summary>
    /// Notes a chain call made on the train itself rather than on the result of an earlier call.
    /// </summary>
    /// <remarks>
    /// Every such call starts from the train's monad, so two of them are two chains unless the
    /// second is reached through the first. A synchronous call before the first junction step is
    /// fine: it has finished by the time the next statement runs. After a junction step that the
    /// body did not await, the next root call runs alongside that junction over the same Memory,
    /// which no replay of the declaration can show, so it is refused. Awaiting the chain first
    /// (<c>await Chain&lt;A&gt;(); Extract&lt;X, Y&gt;();</c>) keeps it one sequence.
    /// </remarks>
    internal void NoteRootCall(string call, bool startsAJunction)
    {
        if (_firstAsyncRootCall is not null && !_unlinkedRefused)
        {
            _unlinkedRefused = true;
            Refuse(
                $"Junctions() calls {call} as a separate statement after {_firstAsyncRootCall}, so "
                    + "the two start separate chains that run at the same time. Link them into one "
                    + "chain, for example Chain<A>().Chain<B>().Resolve()."
            );
        }

        if (startsAJunction)
            _firstAsyncRootCall ??= call;
    }

    /// <summary>
    /// Notes that the body awaited the chain it started, so the next root call follows it
    /// rather than running beside it.
    /// </summary>
    internal void NoteJoined() => _firstAsyncRootCall = null;
}

/// <summary>
/// A refusal as recorded: the step it belongs to, or null for one about the chain as a whole.
/// </summary>
internal readonly record struct RecordedRefusal(
    int? StepIndex,
    ChainStepKind? Kind,
    Type? Junction,
    string Reason
);
