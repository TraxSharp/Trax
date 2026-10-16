using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// A caller's input is read with the host's train parameter options, and an enqueue stores it with
/// <c>TraxJsonSerializationOptions.ManifestProperties</c>, which a worker reads it back with. Both
/// allow 64 levels, so an input a host at depth 64 runs also queues. A host that reads deeper than
/// that can still run such an input, but queuing it is refused as invalid input, not thrown as an
/// unexpected <see cref="JsonException"/>.
/// </summary>
[TestFixture]
public class QueueInputDepthTests
{
    private const int MaxBytes = 262_144;

    private const int Nesting = 12;

    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects
                        .UseInMemory()
                        .SaveTrainParameters(
                            new JsonSerializerOptions(TraxJsonSerializationOptions.Default)
                            {
                                MaxDepth = 64,
                            }
                        )
                )
                .AddMediator(assemblies: [typeof(QueueInputDepthTests).Assembly])
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

    /// <summary>The caller's input: an object nested <see cref="Nesting"/> deep.</summary>
    private static string NestedInputJson(int nesting = Nesting)
    {
        var json = "{\"root\"";
        for (var i = 0; i < nesting; i++)
            json += ":{\"next\"";
        json += ":{" + new string('}', nesting + 2);
        return json;
    }

    private DeepInput NestedInput() =>
        JsonSerializer.Deserialize<DeepInput>(
            NestedInputJson(),
            new JsonSerializerOptions(TraxEffectConfiguration.StaticSystemJsonSerializerOptions)
        )!;

    [Test]
    public async Task RunAsync_InputNestedDeeperThanTheStorageDepth_Succeeds()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await execution.RunAsync(typeof(IDepthTrain).FullName!, NestedInputJson());

        result
            .Output.Should()
            .BeNull(
                "a Unit output comes back null; the run refused nothing, so the host read the "
                    + "input with its own MaxDepth of 64"
            );
    }

    [Test]
    public async Task QueueAsync_InputNestedDeeperThanTheStorageDepth_IsQueuedAndReadsBackAsTheInput()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var queued = await execution.QueueAsync(typeof(IDepthTrain).FullName!, NestedInputJson());

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await context
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == queued.WorkQueueId);

        var entry = TrainInputReader.Read(stored.Input, RegistrationOf<IDepthTrain>(), MaxBytes);
        entry
            .Should()
            .BeEquivalentTo(
                NestedInput(),
                o => o.AllowingInfiniteRecursion(),
                "the stored entry reads back as the input the caller gave"
            );
    }

    [Test]
    public async Task QueueAsync_InputDeeperThanStorageOnAHostThatReadsDeeper_IsRefusedAsInvalidInput()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects
                        .UseInMemory()
                        .SaveTrainParameters(
                            new JsonSerializerOptions(TraxJsonSerializationOptions.Default)
                            {
                                MaxDepth = 128,
                            }
                        )
                )
                .AddMediator(assemblies: [typeof(QueueInputDepthTests).Assembly])
        );
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () => execution.QueueAsync(typeof(IDepthTrain).FullName!, NestedInputJson(70));

        (await act.Should().ThrowAsync<TrainInputValidationException>())
            .Which.InnerException.Should()
            .BeOfType<JsonException>("the stored form is deeper than storage writes");
    }

    public record DeepInput
    {
        public DeepNode? Root { get; init; }
    }

    public record DeepNode
    {
        public DeepNode? Next { get; init; }
    }

    public interface IDepthTrain : IServiceTrain<DeepInput, Unit>;

    public class DepthTrain : ServiceTrain<DeepInput, Unit>, IDepthTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
