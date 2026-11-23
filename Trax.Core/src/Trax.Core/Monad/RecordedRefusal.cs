namespace Trax.Core.Monad;

/// <summary>
/// A refusal as recorded: the step it belongs to, or null for one about the chain as a whole.
/// </summary>
internal readonly record struct RecordedRefusal(
    int? StepIndex,
    ChainStepKind? Kind,
    Type? Junction,
    string Reason
);
