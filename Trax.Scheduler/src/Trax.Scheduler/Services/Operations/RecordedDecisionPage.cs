namespace Trax.Scheduler.Services.Operations;

/// <summary>A page of a run's recorded decisions, in the order they were recorded.</summary>
/// <param name="Items">The decisions on this page.</param>
/// <param name="Take">The page size used, after clamping.</param>
/// <param name="NextCursor">
/// The id of the last decision, to pass as the next call's <c>afterId</c>; null when the page is
/// empty.
/// </param>
public record RecordedDecisionPage(
    IReadOnlyList<RecordedDecisionRecord> Items,
    int Take,
    long? NextCursor
);
