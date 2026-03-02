using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

[TestFixture]
public class TraxEffectConfigurationBuilderTests
{
    [Test]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var builder = new TraxEffectConfigurationBuilder(new ServiceCollection());

        // Assert
        builder.DataContextLoggingEffectEnabled.Should().BeFalse();
        builder.SerializeStepData.Should().BeFalse();
        builder.LogLevel.Should().Be(LogLevel.Debug);
    }

    [Test]
    public void ServiceCollection_ExposedFromConstructor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = new TraxEffectConfigurationBuilder(services);

        // Assert
        builder.ServiceCollection.Should().BeSameAs(services);
    }

    [Test]
    public void SetEffectLogLevel_UpdatesLogLevel()
    {
        // Arrange
        var builder = new TraxEffectConfigurationBuilder(new ServiceCollection());

        // Act
        builder.LogLevel = LogLevel.Trace;

        // Assert
        builder.LogLevel.Should().Be(LogLevel.Trace);
    }

    [Test]
    public void SerializeStepData_SetTrue_IsReflected()
    {
        // Arrange
        var builder = new TraxEffectConfigurationBuilder(new ServiceCollection());

        // Act
        builder.SerializeStepData = true;

        // Assert
        builder.SerializeStepData.Should().BeTrue();
    }

    [Test]
    public void TrainParameterJsonSerializerOptions_DefaultIsNotNull()
    {
        // Arrange & Act
        var builder = new TraxEffectConfigurationBuilder(new ServiceCollection());

        // Assert
        builder.TrainParameterJsonSerializerOptions.Should().NotBeNull();
    }
}
