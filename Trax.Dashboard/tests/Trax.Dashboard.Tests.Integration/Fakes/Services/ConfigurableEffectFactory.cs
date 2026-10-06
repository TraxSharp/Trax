using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Scheduler.Services.Effects;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// A configurable effect factory over a settings object the test holds, so a test can read what
/// the Configure Effect dialog wrote to it.
/// </summary>
public sealed class ConfigurableEffectFactory(object configuration) : IConfigurableProviderFactory
{
    /// <summary>The name the effect settings service gives this effect.</summary>
    public static string FullName => typeof(ConfigurableEffectFactory).FullName!;

    public object GetConfiguration() => configuration;

    public Type GetConfigurationType() => configuration.GetType();

    /// <summary>
    /// Registers an effect registry tracking this factory over <paramref name="configuration"/>,
    /// and the Scheduler's real <see cref="EffectSettingsService"/>, as <c>AddScheduler</c> does.
    /// </summary>
    public static EffectRegistry Register(IServiceCollection services, object configuration)
    {
        var registry = new EffectRegistry();
        registry.Register(typeof(ConfigurableEffectFactory));
        services.AddSingleton<IEffectRegistry>(registry);
        services.AddSingleton(new ConfigurableEffectFactory(configuration));
        services.AddScoped<IEffectSettingsService>(sp => new EffectSettingsService(sp));
        return registry;
    }
}
