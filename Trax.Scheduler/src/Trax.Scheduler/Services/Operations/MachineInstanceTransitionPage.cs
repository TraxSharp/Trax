using Trax.Core.Exceptions;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

/// <summary>A page of a state-machine instance's history, newest first.</summary>
/// <param name="Items">The transitions on this page.</param>
/// <param name="Take">The page size used, after clamping.</param>
/// <param name="NextCursor">
/// The revision to pass as the next page's <c>beforeRevision</c>, or null when this page reaches the oldest one.
/// </param>
public record MachineInstanceTransitionPage(
    IReadOnlyList<MachineInstanceTransitionRecord> Items,
    int Take,
    long? NextCursor
);
