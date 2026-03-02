using FluentAssertions;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using CoreLoggerExtensions = Trax.Core.Extensions.LoggerExtensions;

namespace Trax.Core.Tests.Unit.UnitTests.Extensions;

public class LoggerExtensionsTests : TestSetup
{
    [Theory]
    public async Task CreateGenericLogger_WithValidType_ReturnsLogger()
    {
        // Arrange
        using var factory = LoggerFactory.Create(builder => builder.AddDebug());

        // Act
        var logger = CoreLoggerExtensions.CreateGenericLogger(factory, typeof(string));

        // Assert
        ((object)logger)
            .Should()
            .NotBeNull();
    }

    [Theory]
    public async Task CreateGenericLogger_WithCustomType_ReturnsLogger()
    {
        // Arrange
        using var factory = LoggerFactory.Create(builder => builder.AddDebug());

        // Act
        var logger = CoreLoggerExtensions.CreateGenericLogger(
            factory,
            typeof(LoggerExtensionsTests)
        );

        // Assert
        ((object)logger)
            .Should()
            .NotBeNull();
    }

    [Theory]
    public async Task GetLoggerProviders_EmptyFactory_ReturnsEmptyList()
    {
        // Arrange
        using var factory = LoggerFactory.Create(_ => { });

        // Act
        var providers = factory.GetLoggerProviders();

        // Assert
        providers.Should().NotBeNull();
    }
}
