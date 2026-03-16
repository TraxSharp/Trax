using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

[TestFixture]
public class TraxEffectBuilderTests
{
    [Test]
    public void AddTrax_WithDefaults_RegistersEffectConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax(_ => { });

        // Assert
        var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<ITraxEffectConfiguration>();
        config.Should().NotBeNull();
        config.LogLevel.Should().Be(LogLevel.Debug);
        config.SerializeJunctionData.Should().BeFalse();
    }

    [Test]
    public void AddTrax_WithEffects_RegistersEffectConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.SetEffectLogLevel(LogLevel.Trace))
        );

        // Assert
        var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<ITraxEffectConfiguration>();
        config.LogLevel.Should().Be(LogLevel.Trace);
    }

    [Test]
    public void AddTrax_RegistersEffectRegistry()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddTrax(_ => { });

        // Assert
        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IEffectRegistry>();
        registry.Should().NotBeNull();
    }
}
