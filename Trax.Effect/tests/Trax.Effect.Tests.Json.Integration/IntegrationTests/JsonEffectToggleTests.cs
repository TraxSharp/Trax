using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Json.Services.JsonEffectFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;

namespace Trax.Effect.Tests.Json.Integration.IntegrationTests;

[TestFixture]
public class JsonEffectToggleTests
{
    private ServiceProvider _serviceProvider;
    private const string JsonEffectCategory =
        "Trax.Effect.Provider.Json.Services.JsonEffect.JsonEffectProvider";

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        var services = new ServiceCollection();
        var arrayProvider = new ArrayLoggingProvider();

        services
            .AddSingleton<IArrayLoggingProvider>(arrayProvider)
            .AddLogging(x =>
                x.AddConsole().AddProvider(arrayProvider).SetMinimumLevel(LogLevel.Debug)
            )
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects
                        .SetEffectLogLevel(LogLevel.Information)
                        .AddJson()
                        .AddJunctionLogger(serializeJunctionData: true)
                )
            )
            .AddTransientTraxRoute<IToggleTestTrain, ToggleTestTrain>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await _serviceProvider.DisposeAsync();
    }

    [Test]
    [Order(1)]
    public async Task JsonEffect_EnabledByDefault_ProducesLogs()
    {
        // Arrange
        var arrayProvider = _serviceProvider.GetRequiredService<IArrayLoggingProvider>();
        var jsonLogCountBefore = GetJsonEffectLogCount(arrayProvider);

        // Act
        using var scope = _serviceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IToggleTestTrain>();
        await train.Run(Unit.Default);

        // Assert
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);

        var jsonLogCountAfter = GetJsonEffectLogCount(arrayProvider);
        (jsonLogCountAfter - jsonLogCountBefore)
            .Should()
            .BeGreaterThan(0, "JsonEffect is enabled, so JSON logs should be produced");
    }

    [Test]
    [Order(2)]
    public async Task JsonEffect_DisabledAtRuntime_ProducesNoJsonLogs()
    {
        // Arrange
        var registry = _serviceProvider.GetRequiredService<IEffectRegistry>();
        registry.Disable<JsonEffectProviderFactory>();

        var arrayProvider = _serviceProvider.GetRequiredService<IArrayLoggingProvider>();
        var jsonLogCountBefore = GetJsonEffectLogCount(arrayProvider);

        // Act
        using var scope = _serviceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IToggleTestTrain>();
        await train.Run(Unit.Default);

        // Assert - train still completes successfully
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);

        // But no new JSON logs should have been written
        var jsonLogCountAfter = GetJsonEffectLogCount(arrayProvider);
        (jsonLogCountAfter - jsonLogCountBefore)
            .Should()
            .Be(0, "JsonEffect is disabled, so no JSON logs should be produced");
    }

    [Test]
    [Order(3)]
    public async Task JsonEffect_ReEnabledAtRuntime_ProducesLogsAgain()
    {
        // Arrange
        var registry = _serviceProvider.GetRequiredService<IEffectRegistry>();
        registry.Enable<JsonEffectProviderFactory>();

        var arrayProvider = _serviceProvider.GetRequiredService<IArrayLoggingProvider>();
        var jsonLogCountBefore = GetJsonEffectLogCount(arrayProvider);

        // Act
        using var scope = _serviceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IToggleTestTrain>();
        await train.Run(Unit.Default);

        // Assert
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);

        var jsonLogCountAfter = GetJsonEffectLogCount(arrayProvider);
        (jsonLogCountAfter - jsonLogCountBefore)
            .Should()
            .BeGreaterThan(0, "JsonEffect was re-enabled, so JSON logs should be produced again");
    }

    private static int GetJsonEffectLogCount(IArrayLoggingProvider arrayProvider) =>
        arrayProvider
            .Loggers.Where(logger => logger.Logs.Any(log => log.Category == JsonEffectCategory))
            .SelectMany(logger => logger.Logs.Where(log => log.Category == JsonEffectCategory))
            .Count();

    private class ToggleTestTrain : ServiceTrain<Unit, Unit>, IToggleTestTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();
    }

    private interface IToggleTestTrain : IServiceTrain<Unit, Unit> { }
}
