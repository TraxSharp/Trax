using Trax.Effect.Services.JunctionEffectProvider;

namespace Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;

/// <summary>
/// Service key for the junction logger, so <see cref="JunctionLoggerFactory.JunctionLoggerFactory"/> can
/// resolve it. Infrastructure registered by <c>AddJunctionLogger</c>; not intended for direct use.
/// </summary>
public interface IJunctionLoggerProvider : IJunctionEffectProvider { }
