namespace Trax.Api.DTOs;

/// <summary>
/// One question a run asked a decider, as <c>operations.decisions</c> returns it: the answer the
/// run acted on (or refused) and the tracks routing steps took on it, with what must not be shown
/// left out.
/// </summary>
/// <remarks>
/// Two rules leave values out, the ones a run's steps and its junction events follow. A question
/// about a type marked <c>[TraxSensitive]</c> has its answer withheld (<see cref="AnswerWithheld"/>):
/// <see cref="Answer"/>, <see cref="Refused"/>, <see cref="ReplayRefused"/>, <see cref="Shadows"/> and
/// <see cref="Routes"/> are null. Once a run has taken a track on such a question, every later
/// decision is on a withheld track (<see cref="TrackWithheld"/>), and only <see cref="Id"/>,
/// <see cref="MetadataId"/>, <see cref="Occurrence"/>, <see cref="Replayed"/> and
/// <see cref="DecidedAt"/> are kept.
/// </remarks>
/// <param name="Id">The decision's id; decisions are recorded in id order.</param>
/// <param name="MetadataId">The run that asked.</param>
/// <param name="QuestionKey">The question's key; null on a withheld track.</param>
/// <param name="Occurrence">Which asking of the question this was in the run, from 0.</param>
/// <param name="Kind"><c>choice</c>, <c>score</c> or <c>yes_no</c>; null on a withheld track.</param>
/// <param name="Question">The question as asked, as JSON; null on a withheld track.</param>
/// <param name="Answer">The answer the run acted on (for a refusal, the one it would not act on), as JSON; null when withheld.</param>
/// <param name="Refused">Why the run would not act on the answer; null for an answer it acted on, and when withheld.</param>
/// <param name="IsRefused">True when the run refused the answer and its step failed on it.</param>
/// <param name="Fingerprint">Identifies the asking the answer was given to; null on a withheld track.</param>
/// <param name="Model">The model that answered, when the decider is a model; null on a withheld track.</param>
/// <param name="Decider">The decider's type; null when the answer was replayed, or the track is withheld.</param>
/// <param name="Replayed">True when the answer came from an earlier run rather than a decider.</param>
/// <param name="Shadows">What each shadow decider answered, as JSON; null when withheld.</param>
/// <param name="Routes">The tracks routing steps took on the decision, as a JSON array; null when none did, or when withheld.</param>
/// <param name="StateHash">The hash of the state the question was asked about; null when none was recorded, or the track is withheld.</param>
/// <param name="DecidedAt">When the question was answered (UTC).</param>
/// <param name="AnswerWithheld">True when the question is about a type marked <c>[TraxSensitive]</c>.</param>
/// <param name="TrackWithheld">True when the run had already taken a track on a withheld answer.</param>
public record DecisionRecord(
    long Id,
    long MetadataId,
    string? QuestionKey,
    int Occurrence,
    string? Kind,
    string? Question,
    string? Answer,
    string? Refused,
    bool IsRefused,
    string? Fingerprint,
    string? Model,
    string? Decider,
    bool Replayed,
    string? Shadows,
    string? Routes,
    string? StateHash,
    DateTime DecidedAt,
    bool AnswerWithheld,
    bool TrackWithheld
)
{
    /// <summary>
    /// Why an earlier run's answer was not replayed, so the decider was asked afresh: the
    /// <c>replay_refused</c> member of <see cref="Answer"/>. Null when the answer carries none,
    /// and when the answer is withheld.
    /// </summary>
    public string? ReplayRefused { get; init; }
}

/// <summary>A page of one run's recorded decisions, in the order they were recorded.</summary>
/// <param name="Items">The decisions on this page.</param>
/// <param name="Take">The page size used, after clamping.</param>
/// <param name="NextCursor">
/// The id of the last decision on the page, to pass as the next call's <c>afterId</c>; null when
/// the page is empty.
/// </param>
public record DecisionPage(IReadOnlyList<DecisionRecord> Items, int Take, long? NextCursor);
