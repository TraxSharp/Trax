using Trax.Core.Exceptions;

namespace Trax.Effect.Exceptions;

/// <summary>
/// Fails a run an operator queued to resume an earlier run (<c>resumeExecution</c>) when, as it
/// starts, the resume can no longer be honoured: the checkpoints no longer match the code, a step
/// would read a value nothing restores, or the run it names is not one it may resume. The run is
/// failed with the reason rather than run from the top, because the operator asked to carry on,
/// not to start again. A scheduler's automatic retry is not failed this way; it runs from the top.
/// </summary>
/// <remarks>Classified permanent: running it again hits the same refusal. See Trax.Docs/adr/0047.</remarks>
/// <param name="code">The refusal's stable code (<c>chain-changed</c>, <c>missing-input</c>, <c>different-input</c> and the rest).</param>
/// <param name="message">The refusal, phrased for an operator.</param>
public class ResumeRefusedException(string code, string message) : TrainException(message)
{
    /// <summary>The refusal's stable code.</summary>
    public string Code { get; } = code;
}
