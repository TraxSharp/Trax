using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;

namespace Trax.Mediator.Configuration;

/// <summary>
/// Builder state after <c>AddMediator()</c> has been called.
/// Extension methods for <c>AddScheduler()</c> target this type,
/// enforcing that the mediator is configured before the scheduler.
/// </summary>
public class TraxBuilderWithMediator
{
    internal TraxBuilderWithEffects EffectsBuilder { get; }

    public TraxBuilderWithMediator(TraxBuilderWithEffects effectsBuilder)
    {
        EffectsBuilder = effectsBuilder;
    }

    internal TraxBuilder Root => EffectsBuilder.Root;

    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    public IServiceCollection ServiceCollection => Root.ServiceCollection;
}
