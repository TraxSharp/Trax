using Trax.Effect.Services.JunctionEffectProvider;

namespace Trax.Effect.Services.JunctionEffectProviderFactory;

/// <summary>
/// Creates the <see cref="IJunctionEffectProvider"/> for one train run. Registered as a singleton by
/// <c>AddJunctionEffect</c>; its concrete type is the key the effect registry uses to enable or disable the effect.
/// </summary>
public interface IJunctionEffectProviderFactory
{
    /// <summary>
    /// Creates a new provider. Called once per train run when the effect is enabled; the caller disposes it.
    /// </summary>
    IJunctionEffectProvider Create();
}
