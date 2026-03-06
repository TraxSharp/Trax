using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Json.Services.JsonEffectFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Extensions;

[TestFixture]
public class ServiceExtensionsRegistryTests
{
    [Test]
    public void AddTrax_RegistersIEffectRegistryAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax(trax => trax.AddEffects(_ => { }));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetService<IEffectRegistry>();
        registry.Should().NotBeNull();

        // Verify it's a singleton (same instance)
        var registry2 = provider.GetService<IEffectRegistry>();
        registry.Should().BeSameAs(registry2);
    }

    [Test]
    public void AddEffect_DefaultToggleable_RegistersInRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - AddJson calls AddEffect<JsonEffectProviderFactory>() with default toggleable=true
        services.AddTrax(trax => trax.AddEffects(effects => effects.AddJson()));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<IEffectRegistry>();
        var all = registry.GetAll();

        all.Should().ContainKey(typeof(JsonEffectProviderFactory));
        all[typeof(JsonEffectProviderFactory)].Should().BeTrue();
    }

    [Test]
    public void AddStepEffect_DefaultToggleable_RegistersInRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - AddStepLogger calls AddStepEffect<StepLoggerFactory>() with default toggleable=true
        services.AddTrax(trax => trax.AddEffects(effects => effects.AddStepLogger()));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<IEffectRegistry>();
        var all = registry.GetAll();

        all.Should().ContainKey(typeof(StepLoggerFactory));
        all[typeof(StepLoggerFactory)].Should().BeTrue();
    }

    [Test]
    public void AddTrax_NoEffects_RegistryIsEmpty()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax(trax => trax.AddEffects(_ => { }));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<IEffectRegistry>();
        registry.GetAll().Should().BeEmpty();
    }

    [Test]
    public void AddEffect_Toggleable_AppearsInGetToggleable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTrax(trax => trax.AddEffects(effects => effects.AddJson()));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<IEffectRegistry>();
        var toggleable = registry.GetToggleable();

        toggleable.Should().ContainKey(typeof(JsonEffectProviderFactory));
    }

    [Test]
    public void AddTrax_MultipleEffects_AllRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTrax(trax => trax.AddEffects(effects => effects.AddJson().AddStepLogger()));
        using var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<IEffectRegistry>();
        var all = registry.GetAll();

        all.Should().HaveCount(2);
        all.Should().ContainKey(typeof(JsonEffectProviderFactory));
        all.Should().ContainKey(typeof(StepLoggerFactory));
    }
}
