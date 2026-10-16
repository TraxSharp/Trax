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
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// A caller's input is read with the host's train parameter options, so a host that raised
/// <c>MaxDepth</c> accepts an input the default depth would refuse. An enqueue then stores that
/// input with <c>TraxJsonSerializationOptions.ManifestProperties</c>, whose depth is 8, and a
/// worker reads the stored form back with the same options: the reader accepts more nesting than
/// the storage writes, so a run of such an input succeeds while queuing the same input fails.
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
    private static string NestedInputJson()
    {
        var json = "{\"root\"";
        for (var i = 0; i < Nesting; i++)
            json += ":{\"next\"";
        json += ":{" + new string('}', Nesting + 2);
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
                "the stored entry reads back as the input the caller gave"
            );
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
