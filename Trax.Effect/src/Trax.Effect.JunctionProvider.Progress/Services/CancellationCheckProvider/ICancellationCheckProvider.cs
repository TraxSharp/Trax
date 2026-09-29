using Trax.Effect.Services.JunctionEffectProvider;

namespace Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;

/// <summary>
/// Service key for the between-junction cancellation check, so <c>CancellationCheckFactory</c> can resolve
/// it. Infrastructure registered by <c>AddJunctionProgress</c>; not intended for direct use.
/// </summary>
public interface ICancellationCheckProvider : IJunctionEffectProvider { }
