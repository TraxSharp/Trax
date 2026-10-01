using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Tests.AuthE2E;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// <c>operations.trains</c> is the list a client picks a train from. Every other operations
/// field that takes a train (<c>workQueue.queueTrain</c>, <c>trainStats</c>,
/// <c>executions(trainName:)</c>, <c>workQueues(trainName:)</c>) keys on the interface FullName,
/// so the list has to carry that name for a client to use it.
/// </summary>
[TestFixture]
public class TrainListNameRoundTripTests
{
    [Test]
    public void The_trains_list_carries_the_name_metadata_and_the_work_queue_store()
    {
        var discovery = DiscoveryOf<IEchoTrain, EchoTrain>();

        var info = new OperationsQueries().GetTrains(discovery).Single();

        var values = info.GetType().GetProperties().Select(p => p.GetValue(info) as string);
        values
            .Should()
            .Contain(
                typeof(IEchoTrain).FullName,
                "trainStats and executions(trainName:) match metadata.name, the interface FullName"
            );
    }

    [Test]
    public async Task A_train_picked_from_the_trains_list_can_be_queued()
    {
        var discovery = DiscoveryOf<IEchoTrain, EchoTrain>();
        var execution = Substitute.For<ITrainExecutionService>();
        execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(1, "ext"));
        var operations = new OperationsService(
            discovery,
            Substitute.For<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            execution
        );

        // What a client does: list the trains, pick one, queue it by the name the list gives
        // for that purpose.
        var picked = new OperationsQueries().GetTrains(discovery).Single();
        var response = await new WorkQueueMutations().QueueTrain(
            new QueueTrainInput(picked.FullName, "{\"message\":\"hi\"}"),
            operations,
            default
        );

        response.Success.Should().BeTrue(response.Message);
    }

    private static ITrainDiscoveryService DiscoveryOf<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        var services = new ServiceCollection();
        services.AddScoped<TService, TImplementation>();
        return new TrainDiscoveryService(services);
    }
}
