namespace Trax.Effect.Services.JunctionEffectProviderFactory;

/// <summary>
/// Marks the junction effect that reads a run's database cancel flag before every junction, so a run can be
/// cancelled from another host. Only <c>AddJunctionProgress</c> registers one. A host that lets a state machine
/// invoke trains must register it, because leaving an invoking state cancels its run through that flag, wherever the
/// run executes; the state-machine startup check looks for this marker among the registered junction effects.
/// </summary>
internal interface ICancellationFlagCheckFactory : IJunctionEffectProviderFactory;
