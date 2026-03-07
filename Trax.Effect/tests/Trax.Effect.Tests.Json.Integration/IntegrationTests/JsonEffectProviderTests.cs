using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;

namespace Trax.Effect.Tests.Json.Integration.IntegrationTests;

public class JsonEffectProviderTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.AddTransientTraxRoute<ITestTrain, TestTrain>().BuildServiceProvider();

    [Theory]
    public async Task TestJsonEffect()
    {
        // Arrange
        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();
        var trainTwo = Scope.ServiceProvider.GetRequiredService<ITestTrain>();
        var arrayProvider = Scope.ServiceProvider.GetRequiredService<IArrayLoggingProvider>();

        // Act
        await train.Run(Unit.Default);
        await trainTwo.Run(Unit.Default);

        // Assert
        train.Metadata.Name.Should().Be(typeof(ITestTrain).FullName);
        train.Metadata.FailureException.Should().BeNullOrEmpty();
        train.Metadata.FailureReason.Should().BeNullOrEmpty();
        train.Metadata.FailureStep.Should().BeNullOrEmpty();
        train.Metadata.TrainState.Should().Be(TrainState.Completed);
        arrayProvider.Loggers.Should().NotBeNullOrEmpty();
        arrayProvider.Loggers.Should().HaveCount(6);

        // Verify that we have the expected logger types:
        // 1. Two train loggers (ILogger<ServiceTrain<Unit, Unit>>) - may have empty logs
        // 2. One JsonEffectProvider logger (ILogger<JsonEffectProvider>) - should have JSON logs
        // 3. One LifecycleHookRunner logger (ILogger<LifecycleHookRunner>) - may have empty logs
        var jsonProviderLoggers = arrayProvider
            .Loggers.Where(logger =>
                logger.Logs.Any(log =>
                    log.Category
                    == "Trax.Effect.Provider.Json.Services.JsonEffect.JsonEffectProvider"
                )
            )
            .ToList();

        jsonProviderLoggers
            .Should()
            .HaveCount(
                1,
                "There should be exactly one JsonEffectProvider logger with JSON metadata logs"
            );

        var jsonProviderLogger = jsonProviderLoggers.First();
        jsonProviderLogger
            .Logs.Should()
            .NotBeEmpty("JsonEffectProvider logger should contain JSON metadata logs");
    }

    private class TestTrain : ServiceTrain<Unit, Unit>, ITestTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();
    }

    private interface ITestTrain : IServiceTrain<Unit, Unit> { }
}
