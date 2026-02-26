using Trax.Effect.Services.EffectProviderFactory;

namespace Trax.Effect.Services.StepEffectProviderFactory;

/// <summary>
/// Extends <see cref="IStepEffectProviderFactory"/> with a strongly-typed runtime configuration.
/// Factories implementing this interface expose a singleton configuration object that can be
/// inspected and modified at runtime (e.g. via the dashboard).
/// </summary>
/// <typeparam name="TConfiguration">The configuration POCO type. Must be a reference type so
/// mutations to the singleton are visible to all consumers.</typeparam>
public interface IConfigurableStepEffectProviderFactory<TConfiguration>
    : IStepEffectProviderFactory,
        IConfigurableProviderFactory
    where TConfiguration : class
{
    /// <summary>
    /// The configuration singleton for this factory.
    /// </summary>
    TConfiguration Configuration { get; }

    object IConfigurableProviderFactory.GetConfiguration() => Configuration;
    Type IConfigurableProviderFactory.GetConfigurationType() => typeof(TConfiguration);
}
