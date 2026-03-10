using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

public class PostgresContextTests : TestSetup
{
    [Theory]
    public async Task TestPostgresProviderCanCreateMetadata()
    {
        // Arrange
        var postgresContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        using var context = (IDataContext)postgresContextFactory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestMetadata",
                Input = Unit.Default,
                ExternalId = Guid.NewGuid().ToString("N"),
            }
        );

        await context.Track(metadata);

        await context.SaveChanges(CancellationToken.None);
        context.Reset();

        // Act
        var foundMetadata = await context.Metadatas.FirstOrDefaultAsync(x => x.Id == metadata.Id);

        // Assert
        foundMetadata.Should().NotBeNull();
        foundMetadata.Id.Should().Be(metadata.Id);
        foundMetadata.Name.Should().Be(metadata.Name);
    }

    [Theory]
    public async Task TestPostgresProviderCanRunTrain()
    {
        // Arrange
        // Act
        var train = await TrainBus.RunAsync<TestTrain>(new TestTrainInput());

        // Assert
        var metadata = train!.Metadata!;
        metadata.Name.Should().Be(typeof(ITestTrain).FullName);
        metadata.FailureException.Should().BeNullOrEmpty();
        metadata.FailureReason.Should().BeNullOrEmpty();
        metadata.FailureStep.Should().BeNullOrEmpty();
        metadata.TrainState.Should().Be(TrainState.Completed);
    }

    [Theory]
    public async Task TestPostgresProviderCanRunTrainTwo()
    {
        // Arrange
        // Act
        var train = await TrainBus.RunAsync<TestTrain>(new TestTrainInput());
        var trainTwo = await TrainBus.RunAsync<TestTrainWithoutInterface>(
            new TestTrainWithoutInterfaceInput()
        );

        // Assert
        var metadata = train!.Metadata!;
        metadata.Name.Should().Be(typeof(ITestTrain).FullName);
        metadata.FailureException.Should().BeNullOrEmpty();
        metadata.FailureReason.Should().BeNullOrEmpty();
        metadata.FailureStep.Should().BeNullOrEmpty();
        metadata.TrainState.Should().Be(TrainState.Completed);
    }

    [Theory]
    public async Task TestPostgresProviderCanRunTrainWithinTrain()
    {
        // Arrange
        var dataContextProvider =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var arrayLoggerProvider = Scope.ServiceProvider.GetRequiredService<IArrayLoggingProvider>();

        // Act
        var (innerTrain, train) = await TrainBus.RunAsync<(ITestTrain, ITestTrainWithinTrain)>(
            new TestTrainWithinTrainInput()
        );

        // Assert
        var trainMetadata = train!.Metadata!;
        trainMetadata.Name.Should().Be(typeof(ITestTrainWithinTrain).FullName);
        trainMetadata.FailureException.Should().BeNullOrEmpty();
        trainMetadata.FailureReason.Should().BeNullOrEmpty();
        trainMetadata.FailureStep.Should().BeNullOrEmpty();
        trainMetadata.TrainState.Should().Be(TrainState.Completed);
        var innerTrainMetadata = innerTrain!.Metadata!;
        innerTrainMetadata.Name.Should().Be(typeof(ITestTrain).FullName);
        innerTrainMetadata.FailureException.Should().BeNullOrEmpty();
        innerTrainMetadata.FailureReason.Should().BeNullOrEmpty();
        innerTrainMetadata.FailureStep.Should().BeNullOrEmpty();
        innerTrainMetadata.TrainState.Should().Be(TrainState.Completed);

        using var dataContext = (IDataContext)dataContextProvider.Create();

        var parentTrainResult = await dataContext.Metadatas.FirstOrDefaultAsync(x =>
            x.Id == trainMetadata.Id
        );
        var childTrainResult = await dataContext.Metadatas.FirstOrDefaultAsync(x =>
            x.Id == innerTrainMetadata.Id
        );
        parentTrainResult.Should().NotBeNull();
        parentTrainResult!.Id.Should().Be(trainMetadata.Id);
        parentTrainResult!.TrainState.Should().Be(TrainState.Completed);
        parentTrainResult.Input.Should().NotBeNull();
        parentTrainResult.Output.Should().NotBeNull();

        childTrainResult.Should().NotBeNull();
        childTrainResult!.Id.Should().Be(innerTrainMetadata.Id);
        childTrainResult.TrainState.Should().Be(TrainState.Completed);
        childTrainResult.Input.Should().NotBeNull();
        childTrainResult.Output.Should().NotBeNull();

        var logLevel = arrayLoggerProvider
            .Loggers.SelectMany(x => x.Logs)
            .Select(x => x.Level)
            .Count(x => x == LogLevel.Critical);
        logLevel.Should().Be(1);
    }

    internal class TestTrain : ServiceTrain<TestTrainInput, TestTrain>, ITestTrain
    {
        protected override async Task<Either<Exception, TestTrain>> RunInternal(
            TestTrainInput input
        ) => Activate(input, this).Resolve();
    }

    internal class TestTrainWithoutInterface
        : ServiceTrain<TestTrainWithoutInterfaceInput, TestTrainWithoutInterface>
    {
        protected override async Task<Either<Exception, TestTrainWithoutInterface>> RunInternal(
            TestTrainWithoutInterfaceInput input
        ) => Activate(input, this).Resolve();
    }

    internal record TestTrainWithoutInterfaceInput;

    internal record TestTrainInput;

    internal class TestTrainWithinTrain()
        : ServiceTrain<TestTrainWithinTrainInput, (ITestTrain, ITestTrainWithinTrain)>,
            ITestTrainWithinTrain
    {
        protected override async Task<
            Either<Exception, (ITestTrain, ITestTrainWithinTrain)>
        > RunInternal(TestTrainWithinTrainInput input) =>
            Activate(input)
                .AddServices<ITestTrainWithinTrain>(this)
                .Chain<StepToRunTestTrain>()
                .Resolve();
    }

    internal record TestTrainWithinTrainInput;

    internal class StepToRunTestTrain(ITrainBus trainBus, ILogger<StepToRunTestTrain> logger)
        : EffectStep<Unit, ITestTrain>
    {
        public override async Task<ITestTrain> Run(Unit input)
        {
            var testTrain = await trainBus.RunAsync<TestTrain>(new TestTrainInput());

            logger.LogCritical("Ran {TrainName}", "TestTrain");

            return testTrain;
        }
    }

    internal interface ITestTrain : IServiceTrain<TestTrainInput, TestTrain> { }

    internal interface ITestTrainWithinTrain
        : IServiceTrain<TestTrainWithinTrainInput, (ITestTrain, ITestTrainWithinTrain)> { }
}
