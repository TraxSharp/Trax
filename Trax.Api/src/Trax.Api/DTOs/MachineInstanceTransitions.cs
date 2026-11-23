using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>
/// A page of one state-machine instance's history, newest first, as <c>machineInstance { transitions }</c> reads it.
/// Pass <see cref="NextCursor"/> as the next request's <c>after</c> to read older transitions.
/// </summary>
/// <param name="Items">The transitions on this page, newest first.</param>
/// <param name="NextCursor">The revision to pass as <c>after</c> for the next page, or <see langword="null"/> on the last page.</param>
public record MachineInstanceTransitions(
    IReadOnlyList<MachineInstanceTransition> Items,
    long? NextCursor
)
{
    internal static MachineInstanceTransitions From(MachineInstanceTransitionPage? page) =>
        page is null
            ? new([], NextCursor: null)
            : new(page.Items.Select(MachineInstanceTransition.From).ToList(), page.NextCursor);
}
