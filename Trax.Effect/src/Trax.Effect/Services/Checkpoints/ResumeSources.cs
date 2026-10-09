using Trax.Effect.Enums;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Whether a run may carry on from the run it names to resume, decided from that run alone before
/// any checkpoint of it is trusted: the codes a refusal carries, and the check itself.
/// </summary>
/// <remarks>
/// The scheduler's retries and the operator's resume choose a run that passes these checks, but
/// the link is a column any enqueue can set, so the resumed run checks it again as it starts.
/// See Trax.Docs/adr/0047.
/// </remarks>
internal static class ResumeSources
{
    /// <summary>No run with that id exists.</summary>
    public const string UnknownRun = "unknown-run";

    /// <summary>The run is a run of another train.</summary>
    public const string AnotherTrain = "another-train";

    /// <summary>The run did not fail and was not cancelled, so it has nothing left to resume.</summary>
    public const string NotFailed = "not-failed";

    /// <summary>A resume of the run already completed, so its work is done.</summary>
    public const string AlreadyResumed = "already-resumed";

    /// <summary>The run ran on another input than the run resuming it.</summary>
    public const string DifferentInput = "different-input";

    /// <summary>The reason a second resume of a run some resume already completed is refused.</summary>
    public static string AlreadyResumedReason(long runId) =>
        $"a resume of run {runId} already completed, so its work is done. Requeue it to run it "
        + "again from the top.";

    /// <summary>
    /// Why the run <paramref name="from"/> may not be resumed by a run of
    /// <paramref name="train"/> on <paramref name="input"/>, or null when it may.
    /// </summary>
    /// <param name="from">The run named to resume.</param>
    /// <param name="source">That run as stored, or null when it does not exist.</param>
    /// <param name="train">The resuming run's train name.</param>
    /// <param name="storedInput">The resuming run's input as stored on its row.</param>
    /// <param name="input">The resuming run's input.</param>
    /// <param name="inputType">The train's input type.</param>
    public static (string Code, string Reason)? Refusal(
        long from,
        ResumeSource? source,
        string train,
        string? storedInput,
        object? input,
        Type inputType
    ) =>
        source switch
        {
            null => (UnknownRun, $"no run {from} exists."),
            { Name: var name } when !string.Equals(name, train, StringComparison.Ordinal) => (
                AnotherTrain,
                $"run {from} is a run of train '{name}', not of this train."
            ),
            { State: not (TrainState.Failed or TrainState.Cancelled) } => (
                NotFailed,
                $"run {from} is {source.State}; only a failed or cancelled run can be resumed."
            ),
            { ResumedToCompletion: true } => (AlreadyResumed, AlreadyResumedReason(from)),
            _ when !RunInputs.Same(source.Input, storedInput, input, inputType) => (
                DifferentInput,
                $"run {from} ran on a different input, so its checkpoints hold state computed "
                    + "for another request."
            ),
            _ => null,
        };
}
