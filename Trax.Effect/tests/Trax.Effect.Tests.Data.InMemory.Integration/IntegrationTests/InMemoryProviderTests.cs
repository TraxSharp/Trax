using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

public class InMemoryProviderTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.AddScopedTraxRoute<ITestTrain, TestTrain>().BuildServiceProvider();

    [Test]
    [Ignore("Serialization Failing for Input/Output Objects.")]
    public async Task TestInMemoryProviderCanCreateMetadata()
    {
        // Arrange
        var inMemoryContextFactory =
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        var context = (IDataContext)inMemoryContextFactory.Create();

        var metadata = Metadata.Create(
            new CreateMetadata()
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

    [Test]
    [Ignore("Serialization Failing for Input/Output Objects.")]
    public async Task TestInMemoryProviderCanRunTrain()
    {
        // Arrange

        var train = Scope.ServiceProvider.GetRequiredService<ITestTrain>();

        // Act
        await train.Run(Unit.Default);

        // Assert
        train.Metadata.Name.Should().Be("TestTrain");
        train.Metadata.FailureException.Should().BeNullOrEmpty();
        train.Metadata.FailureReason.Should().BeNullOrEmpty();
        train.Metadata.FailureStep.Should().BeNullOrEmpty();
        train.Metadata.TrainState.Should().Be(TrainState.Completed);
    }

    private class TestTrain : ServiceTrain<Unit, Unit>, ITestTrain
    {
        protected override async Task<Either<Exception, Unit>> RunInternal(Unit input) =>
            Activate(input).Resolve();
    }

    private interface ITestTrain : IServiceTrain<Unit, Unit> { }
}
