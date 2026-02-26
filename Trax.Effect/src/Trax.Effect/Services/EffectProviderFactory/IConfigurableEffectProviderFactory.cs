namespace Trax.Effect.Services.EffectProviderFactory;

/// <summary>
/// Extends <see cref="IEffectProviderFactory"/> with a strongly-typed runtime configuration.
/// Factories implementing this interface expose a singleton configuration object that can be
/// inspected and modified at runtime (e.g. via the dashboard).
/// </summary>
/// <typeparam name="TConfiguration">The configuration POCO type. Must be a reference type so
/// mutations to the singleton are visible to all consumers.</typeparam>
public interface IConfigurableEffectProviderFactory<TConfiguration>
    : IEffectProviderFactory,
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
