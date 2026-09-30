using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// How a run and an enqueue go from a caller's request to a train: the scope a run resolves its
/// train in, how the output is read back, what is written before the train exists, how the
/// caller's input is read and stored, and what the host is told when no train takes an input.
/// </summary>
[TestFixture]
public partial class RunAndQueueExecutionTests
{
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AsyncOnlyResource>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(RunAndQueueExecutionTests).Assembly])
        );

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _serviceProvider.DisposeAsync();
        TrainBus.ClearMethodCache();
    }
}
