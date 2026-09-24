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
    private readonly List<ChainStep> _steps = [];

    private readonly List<string> _refusals = [];

    /// <summary>The steps declared, in the order the chain declares them.</summary>
    public IReadOnlyList<ChainStep> Steps => _steps;

    /// <summary>
    /// Things the declaration did that no step can express, such as stating its result directly
    /// or awaiting work, each phrased for whoever has to fix it.
    /// </summary>
    public IReadOnlyList<string> Refusals => _refusals;

    internal void Record(ChainStepKind kind, Type? junction, Type? tIn, Type? tOut) =>
        _steps.Add(new ChainStep(kind, junction, tIn, tOut));

    internal void Refuse(string reason) => _refusals.Add(reason);
}
