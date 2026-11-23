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

    /// <summary>
    /// Runs a fixed set of named branches side by side, each on its own copy of Memory, and
    /// merges what they produce. <see cref="ChainRecorder.TracksAt(int)"/> holds the branches.
    /// </summary>
    Parallel,

    /// <summary>
    /// Stores the state type in Memory the chain names, so a later run can resume after it. The
    /// step's input is the state; it contributes nothing to Memory.
    /// </summary>
    Checkpoint,
}
