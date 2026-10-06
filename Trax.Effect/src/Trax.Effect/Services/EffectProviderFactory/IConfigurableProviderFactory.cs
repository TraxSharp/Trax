namespace Trax.Effect.Services.EffectProviderFactory;

/// <summary>
/// Non-generic marker interface for effect provider factories that expose runtime-configurable settings.
/// Allows the dashboard to detect and interact with configurable factories without knowing
/// the configuration type at compile time.
/// </summary>
public interface IConfigurableProviderFactory
{
    /// <summary>
    /// Returns the configuration object for this factory.
    /// </summary>
    object GetConfiguration();

    /// <summary>
    /// Returns the concrete type of the configuration object.
    /// </summary>
    Type GetConfigurationType();
}
