using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// A run's input saved with reference metadata, as <c>SaveTrainParameters()</c> saved every input
/// until the default options stopped preserving references, read back from the <c>jsonb</c> input
/// column, resolves to the input the run was given. <c>jsonb</c> does not keep
/// property order: it hands an object back with its shorter names first, so an input with an
/// <c>Id</c> property comes back as <c>{"id": .., "$id": ..}</c>, with the reference metadata
/// no longer the first property.
/// </summary>
public class SavedInputJsonbTests : TestSetup
{
    private const int MaxBytes = 262_144;

    [Test]
    public async Task SavedInput_ReadBackFromJsonb_WithAnIdPropertyASharedObjectAndALists_ResolvesExactly()
    {
        var shared = new Spot { Id = 7, City = "Lyon" };
        var original = new SpotsInput
        {
            Id = 5,
            Home = shared,
            Work = shared,
            Stops = [new Spot { Id = 1, City = "Paris" }, shared],
        };

        // Saved the way SaveTrainParameters() saved every input until TraxJsonSerializationOptions
        // stopped preserving references: those rows stay in the column and must still resolve.
        var legacy = new JsonSerializerOptions(TraxJsonSerializationOptions.Default)
        {
            ReferenceHandler = ReferenceHandler.Preserve,
        };
        var row = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISpotsTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        row.Input = JsonSerializer.Serialize(original, legacy);

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using (var context = (IDataContext)factory.Create())
        {
            await context.Track(row);
            await context.SaveChanges(CancellationToken.None);
        }

        string saved;
        using (var context = (IDataContext)factory.Create())
        {
            saved = (
                await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == row.Id)
            ).Input!;
        }

        // What makes this test worth having: the column reordered the metadata away from the
        // front of the root and of every object with a shorter property, and still carries it.
        saved.Should().NotStartWith("{\"$id\"");
        saved.Should().StartWith("{\"id\": 5, \"$id\"");
        saved.Should().Contain("\"$ref\"").And.Contain("\"$values\"");

        var registration = Scope
            .ServiceProvider.GetRequiredService<ITrainDiscoveryService>()
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(ISpotsTrain));

        var read = (SpotsInput)
            TrainInputReader.Read(
                TrainInputReader.ResolveSavedInput(saved, registration, MaxBytes),
                registration,
                MaxBytes
            );

        read.Should().BeEquivalentTo(original);
        read.Work.Should()
            .BeEquivalentTo(
                shared,
                "the second occurrence of the shared object is a $ref, which must read back as the "
                    + "object it names, not as one with every member at its default"
            );
    }

    public class Spot
    {
        public int Id { get; set; }
        public string? City { get; set; }
    }

    public class SpotsInput
    {
        public int Id { get; set; }
        public Spot? Home { get; set; }
        public Spot? Work { get; set; }
        public List<Spot> Stops { get; set; } = [];
    }

    public interface ISpotsTrain : IServiceTrain<SpotsInput, Unit>;

    public class SpotsTrain : ServiceTrain<SpotsInput, Unit>, ISpotsTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
