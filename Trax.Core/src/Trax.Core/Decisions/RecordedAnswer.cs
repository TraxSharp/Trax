namespace Trax.Core.Decisions;

/// <summary>
/// An answer an earlier run acted on, as a host recorded it.
/// </summary>
/// <param name="Answer">The answer, as <see cref="DecisionMade.Answer"/> carried it.</param>
/// <param name="Fingerprint">
/// The <see cref="DecisionMade.Fingerprint"/> the answer was recorded with, exactly as given.
/// </param>
public sealed record RecordedAnswer(Answer Answer, string Fingerprint)
{
    /// <summary>
    /// The <see cref="DecisionMade.StateHash"/> the answer was recorded with, exactly as given.
    /// An answer is replayed only when it equals the hash of the state asked about now, so one
    /// left null, such as an answer recorded before states were hashed, is never replayed.
    /// </summary>
    public string? StateHash { get; init; }
}
