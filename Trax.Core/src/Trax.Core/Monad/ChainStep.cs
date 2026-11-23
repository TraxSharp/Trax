namespace Trax.Core.Monad;

/// <summary>
/// One declared step of a train's junction chain, carrying only types. A step is recorded rather than
/// executed, so a chain can be read at startup without running any of the work it describes.
/// </summary>
/// <param name="Kind">Which chain primitive declared this step.</param>
/// <param name="Junction">The junction type, or null for steps that name no junction.</param>
/// <param name="In">The type the step consumes from Memory, or null when it consumes nothing.</param>
/// <param name="Out">The type the step contributes to Memory, or null when it contributes nothing.</param>
public readonly record struct ChainStep(ChainStepKind Kind, Type? Junction, Type? In, Type? Out);
