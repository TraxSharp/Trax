using Trax.Effect.Services.ChangeSignal;

namespace Trax.Api.DTOs;

/// <summary>
/// Payload for the <c>onDataChanged</c> subscription. Carries only which domain changed, not the
/// changed rows: a subscriber uses it as a nudge to refetch its (bounded, paged) view. Signals are
/// coalesced server-side, so one event stands in for a whole burst of writes to that domain.
/// </summary>
public record DataChangedEvent(ChangeDomain Domain, DateTime Timestamp);
