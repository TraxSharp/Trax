using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Samples.Api.Trains.HelloWorld;

namespace Trax.Samples.Api.Tests.IntegrationTests;

/// <summary>
/// Runs a train the way the application does, through the train bus, on a container built
/// with the same Trax registration as Program.cs and the in-memory data provider, then reads
/// the record Trax kept of the run.
/// </summary>
[TestFixture]
public class HelloWorldTrainTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(HelloWorldTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task RunAsync_WithAName_CompletesAndIsRecorded()
    {
        // A name no other run uses, to find this run's record among any others.
        var name = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        await bus.RunAsync<Unit>(new HelloWorldInput { Name = name });

        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var run = data
            .Metadatas.Where(m => m.Name == typeof(IHelloWorldTrain).FullName)
            .AsEnumerable()
            .SingleOrDefault(m => m.Input?.Contains(name) == true);

        Assert.That(run, Is.Not.Null, "every run of a train is recorded, with its input");
        Assert.That(run!.TrainState, Is.EqualTo(TrainState.Completed));
    }
}
