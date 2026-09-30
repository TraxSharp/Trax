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
}

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
/// here and returning immediately. Nothing is resolved from the container and no junction runs,
/// so reading a chain is safe to do at host startup for every registered train.
/// </remarks>
public sealed class ChainRecorder
{
    internal ChainRecorder() { }

    private readonly List<ChainStep> _steps = [];

    private readonly List<string> _refusals = [];

    private readonly List<RecordedRefusal> _recordedRefusals = [];

    private readonly System.Collections.Generic.HashSet<int> _builtSteps = [];

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
