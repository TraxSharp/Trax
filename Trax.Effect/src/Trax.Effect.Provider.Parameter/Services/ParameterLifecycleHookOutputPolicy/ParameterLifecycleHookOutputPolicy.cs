using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.LifecycleHookOutputPolicy;

namespace Trax.Effect.Provider.Parameter.Services.ParameterLifecycleHookOutputPolicy;

/// <summary>
/// Gives lifecycle hooks the copy of a train's output that the parameter effect's configuration
/// allows: none for an output it excludes, and one bounded by <c>MaxParameterBytes</c> otherwise.
/// </summary>
/// <remarks>
/// An excluded output (<c>ExcludeOutput</c>, <c>ShouldSaveOutputs</c>, or <c>SaveOutputs = false</c>)
/// is one the host chose not to serialize, usually because it is large, so the hooks do not
/// serialize it either. With the effect switched off at runtime nothing is stored, and the hooks
/// get a copy as on a host without the effect, under <c>MaxParameterBytes</c> when it is set.
/// </remarks>
internal sealed class ParameterLifecycleHookOutputPolicy(
    ParameterEffectConfiguration configuration,
    IEffectRegistry effectRegistry
) : ILifecycleHookOutputPolicy
{
    public int? MaxCopyBytes(string trainName)
    {
        var ceiling =
            configuration.MaxParameterBytes ?? DefaultLifecycleHookOutputPolicy.DefaultMaxCopyBytes;

        if (
            !effectRegistry.IsEnabled<ParameterEffectProviderFactory.ParameterEffectProviderFactory>()
        )
            return ceiling;

        return configuration.SaveOutputs && configuration.ShouldSaveOutputFor(trainName)
            ? ceiling
            : null;
    }
}
