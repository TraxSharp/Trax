using System.Text;
using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// A run's input saved by <c>SaveTrainParameters()</c> reads back, through
/// <see cref="TrainInputReader"/>, as exactly the input the run was given. The saved form carries
/// System.Text.Json reference metadata (<c>$id</c>, <c>$values</c>, <c>$ref</c>), which a
/// caller's input is read without, so a re-queue resolves it first.
/// </summary>
[TestFixture]
public class SavedInputRoundTripTests
{
    private const int MaxBytes = 262_144;

    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(assemblies: [typeof(SavedInputRoundTripTests).Assembly])
        );
        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        TrainBus.ClearMethodCache();
    }

    private TrainRegistration RegistrationOf<TTrain>() =>
        _serviceProvider
            .GetRequiredService<ITrainDiscoveryService>()
            .DiscoverTrains()
            .Single(r => r.ServiceType == typeof(TTrain));

    /// <summary>The input as the parameter effect a host with SaveTrainParameters() saves it.</summary>
    private async Task<string> SaveAsync(object input)
    {
        var factory = _serviceProvider.GetRequiredService<ParameterEffectProviderFactory>();
        using var effect = factory.Create();

        var metadata = new Metadata { Name = "saved-input" };
        metadata.SetInputObject(input);
        await effect.Track(metadata);
        await effect.SaveChanges(CancellationToken.None);

        return metadata.Input!;
    }

    private object ReadSaved<TTrain>(string saved)
    {
        var registration = RegistrationOf<TTrain>();
        return TrainInputReader.Read(
            TrainInputReader.ResolveSavedInput(saved, registration, MaxBytes),
            registration,
            MaxBytes
        );
    }

    private static ShapesInput Shapes()
    {
        var shared = new Place { City = "Lyon", Zip = 69001 };
        return new ShapesInput
        {
            Counts = [3, 1, 4, 1, 5],
            Tags = ["alpha", "beta"],
            Nested = new Nested
            {
                Label = "outer",
                Places = [new Place { City = "Paris", Zip = 75001 }, shared],
                Matrix =
                [
                    [1, 2],
                    [3],
                ],
            },
            Home = shared,
            Billing = shared,
        };
    }

    [Test]
    public async Task SavedInput_WithListsArraysNestingAndASharedObject_ReadsBackExactly()
    {
        var original = Shapes();
        var saved = await SaveAsync(original);

        // The save path really does write reference metadata, or this test proves nothing.
        saved.Should().Contain("\"$id\"").And.Contain("\"$values\"").And.Contain("\"$ref\"");

        var read = (ShapesInput)ReadSaved<IShapesTrain>(saved);

        read.Counts.Should().Equal(3, 1, 4, 1, 5);
        read.Tags.Should().Equal("alpha", "beta");
        read.Nested!.Label.Should().Be("outer");
        read.Nested.Places.Should().BeEquivalentTo(original.Nested!.Places);
        read.Nested.Matrix.Should().HaveCount(2);
        read.Nested.Matrix[0].Should().Equal(1, 2);
        read.Nested.Matrix[1].Should().Equal(3);
        read.Home.Should().BeEquivalentTo(new Place { City = "Lyon", Zip = 69001 });
        read.Billing.Should()
            .BeEquivalentTo(
                new Place { City = "Lyon", Zip = 69001 },
                "the second occurrence of the shared object is a $ref, which must read back as "
                    + "the object it names, not as one with every member at its default"
            );
        read.Should().BeEquivalentTo(original);
    }

    [Test]
    public async Task SavedInput_HoldingTheSameObjectTwice_ReadsTheSecondCopyBackWithItsValues()
    {
        // No list here, so nothing refuses the saved form: read without resolving it, the $ref
        // standing in for the second copy reads back silently as an object at its defaults.
        var shared = new Place { City = "Lyon", Zip = 69001 };
        var saved = await SaveAsync(new PairInput { Home = shared, Billing = shared });
        saved.Should().Contain("\"$ref\"");

        var read = (PairInput)ReadSaved<IPairTrain>(saved);

        read.Home.Should().BeEquivalentTo(shared);
        read.Billing.Should().BeEquivalentTo(shared);
    }

    [Test]
    public async Task SavedInput_OfAPositionalRecord_ReadsBackExactly()
    {
        var shared = new Place { City = "Nice", Zip = 6000 };
        var original = new RecordInput("r-1", [7, 8, 9], ["x"], shared, shared);

        var saved = await SaveAsync(original);
        saved.Should().StartWith("{").And.Contain("\"$id\"");

        var read = (RecordInput)ReadSaved<IRecordTrain>(saved);

        read.Id.Should().Be("r-1");
        read.Values.Should().Equal(7, 8, 9);
        read.Labels.Should().Equal("x");
        read.Primary.Should().BeEquivalentTo(shared);
        read.Secondary.Should().BeEquivalentTo(shared);
    }

    [Test]
    public async Task SavedInput_ResolvedFormIsATree_NoTwoMembersShareAnObject()
    {
        var saved = await SaveAsync(Shapes());

        var read = (ShapesInput)ReadSaved<IShapesTrain>(saved);

        read.Home.Should().NotBeSameAs(read.Billing);
    }

    [Test]
    public async Task SavedInput_QueuesThroughTheMediatorAsTheRunsInput()
    {
        var saved = await SaveAsync(Shapes());
        var registration = RegistrationOf<IShapesTrain>();

        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var queued = await execution.QueueAsync(
            typeof(IShapesTrain).FullName!,
            TrainInputReader.ResolveSavedInput(saved, registration, MaxBytes)
        );

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await context
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == queued.WorkQueueId);

        var entry = TrainInputReader.Read(stored.Input, registration, MaxBytes);
        entry.Should().BeEquivalentTo(Shapes());
    }

    [TestCase("""{"counts":[1,2],"tags":["a"],"home":{"city":"Lyon","zip":1}}""")]
    [TestCase("""{"Counts":[1,2],"Tags":["a"],"Home":{"City":"Lyon","Zip":1}}""")]
    public void PlainCallerJson_IsLeftAsItIs_AndStillReads(string json)
    {
        var registration = RegistrationOf<IShapesTrain>();

        TrainInputReader.ResolveSavedInput(json, registration, MaxBytes).Should().Be(json);

        var read = (ShapesInput)TrainInputReader.Read(json, registration, MaxBytes);
        read.Counts.Should().Equal(1, 2);
        read.Tags.Should().Equal("a");
        read.Home.Should().BeEquivalentTo(new Place { City = "Lyon", Zip = 1 });
    }

    [Test]
    public void PlainCallerJson_ForAPositionalRecord_StillReads()
    {
        // Reading with System.Text.Json's own reference handling would refuse this: it does not
        // support metadata on constructor parameters, and a caller never sends any.
        const string json =
            """{"id":"r-2","values":[1],"labels":[],"primary":{"city":"A","zip":1},"secondary":{"city":"B","zip":2}}""";
        var registration = RegistrationOf<IRecordTrain>();

        var read = (RecordInput)
            TrainInputReader.Read(
                TrainInputReader.ResolveSavedInput(json, registration, MaxBytes),
                registration,
                MaxBytes
            );

        read.Values.Should().Equal(1);
        read.Secondary.City.Should().Be("B");
    }

    [Test]
    public void CallerJson_WithReferenceMetadataBelowTheRoot_IsNotResolved()
    {
        // Reference metadata in a caller's input keeps meaning nothing: only a root that opens
        // with $id, the form a saved input always has, is resolved.
        const string json = """{"home":{"$id":"1","city":"Lyon"},"billing":{"$ref":"1"}}""";
        var registration = RegistrationOf<IShapesTrain>();

        TrainInputReader.ResolveSavedInput(json, registration, MaxBytes).Should().Be(json);
    }

    [Test]
    public void SavedInput_WhoseReferencesCopyOneValueManyTimes_IsRefusedAtTheCap()
    {
        var row = string.Join(",", Enumerable.Repeat("1", 500));
        var refs = string.Join(",", Enumerable.Repeat("""{"$ref":"3"}""", 2_000));
        var saved =
            """{"$id":"1","matrix":{"$id":"2","$values":[{"$id":"3","$values":["""
            + row
            + "]},"
            + refs
            + "]}}";
        Encoding.UTF8.GetByteCount(saved).Should().BeLessThan(64 * 1024);

        var registration = RegistrationOf<IShapesTrain>();
        var act = () => TrainInputReader.ResolveSavedInput(saved, registration, MaxBytes);

        var refused = act.Should().Throw<TrainInputValidationException>().Which;
        refused.MaxBytes.Should().Be(MaxBytes);
    }

    [TestCase("""{"$id":"1","nested":{"$id":"2","places":{"$ref":"2"}}}""")]
    [TestCase("""{"$id":"1","home":{"$ref":"9"}}""")]
    [TestCase("""{"$id":"1","home":{"$ref":"1","city":"x"}}""")]
    [TestCase("""{"$id":"1","home":{"$id":"1"}}""")]
    public void SavedInput_WithMetadataThatHasNoPlainForm_IsRefused(string saved)
    {
        var registration = RegistrationOf<IShapesTrain>();

        var act = () => TrainInputReader.ResolveSavedInput(saved, registration, MaxBytes);

        act.Should().Throw<JsonException>();
    }

    public class Place
    {
        public string? City { get; set; }
        public int Zip { get; set; }
    }

    public class Nested
    {
        public string? Label { get; set; }
        public List<Place> Places { get; set; } = [];
        public List<List<int>> Matrix { get; set; } = [];
    }

    public class ShapesInput
    {
        public List<int> Counts { get; set; } = [];
        public string[] Tags { get; set; } = [];
        public Nested? Nested { get; set; }
        public Place? Home { get; set; }
        public Place? Billing { get; set; }
        public List<List<int>> Matrix { get; set; } = [];
    }

    public interface IShapesTrain : IServiceTrain<ShapesInput, Unit>;

    public class ShapesTrain : ServiceTrain<ShapesInput, Unit>, IShapesTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public class PairInput
    {
        public Place? Home { get; set; }
        public Place? Billing { get; set; }
    }

    public interface IPairTrain : IServiceTrain<PairInput, Unit>;

    public class PairTrain : ServiceTrain<PairInput, Unit>, IPairTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public record RecordInput(
        string Id,
        List<int> Values,
        string[] Labels,
        Place Primary,
        Place Secondary
    );

    public interface IRecordTrain : IServiceTrain<RecordInput, Unit>;

    public class RecordTrain : ServiceTrain<RecordInput, Unit>, IRecordTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
