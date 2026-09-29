using Trax.Effect.Services.JunctionEffectProvider;

namespace Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;

/// <summary>
/// Service key for junction progress, so <c>JunctionProgressFactory</c> can resolve it. Infrastructure
/// registered by <c>AddJunctionProgress</c>; not intended for direct use.
/// </summary>
public interface IJunctionProgressProvider : IJunctionEffectProvider { }
