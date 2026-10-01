using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Lambda.Extensions;
using Trax.Scheduler.Lambda.Services;
using Trax.Scheduler.Sqs.Extensions;
using Trax.Scheduler.Sqs.Services;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// <c>UseLambdaWorkers</c> and <c>UseSqsWorkers</c> can each be called more than once. Each call
/// routes its trains to its own function or queue, through its own client.
/// </summary>
[TestFixture]
public class MultipleAwsWorkerRegistrationsTests
{
    [Test]
    public void Two_lambda_registrations_route_each_train_to_its_own_function_and_client()
    {
        var configured = new List<string>();
        using var provider = BuildProvider(scheduler =>
            scheduler
                .UseLambdaWorkers(
                    o =>
                    {
                        o.FunctionName = "gpu-function";
                        o.ConfigureLambdaClient = c =>
                        {
                            c.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
                            configured.Add("gpu");
                        };
                    },
                    routing => routing.ForTrain<IFirstTrain>()
                )
                .UseLambdaWorkers(
                    o =>
                    {
                        o.FunctionName = "cpu-function";
                        o.ConfigureLambdaClient = c =>
                        {
                            c.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
                            configured.Add("cpu");
                        };
                    },
                    routing => routing.ForTrain<ISecondTrain>()
                )
        );

        var routing = provider.GetRequiredService<JobSubmitterRoutingConfiguration>();
        routing
            .GetRegistration(typeof(IFirstTrain).FullName!)!
            .Description.Should()
            .Contain("gpu-function");
        routing
            .GetRegistration(typeof(ISecondTrain).FullName!)!
            .Description.Should()
            .Contain("cpu-function");

        using var scope = provider.CreateScope();
        routing
            .ResolveSubmitter(scope.ServiceProvider, typeof(IFirstTrain).FullName!)
            .Should()
            .BeOfType<LambdaJobSubmitter>();
        routing
            .ResolveSubmitter(scope.ServiceProvider, typeof(ISecondTrain).FullName!)
            .Should()
            .BeOfType<LambdaJobSubmitter>();

        configured
            .Should()
            .BeEquivalentTo(["gpu", "cpu"], "each registration builds its own client");
    }

    [Test]
    public void Two_sqs_registrations_route_each_train_to_its_own_queue_and_client()
    {
        var configured = new List<string>();
        using var provider = BuildProvider(scheduler =>
            scheduler
                .UseSqsWorkers(
                    o =>
                    {
                        o.QueueUrl = "https://sqs.test/first";
                        o.ConfigureSqsClient = c =>
                        {
                            c.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
                            configured.Add("first");
                        };
                    },
                    routing => routing.ForTrain<IFirstTrain>()
                )
                .UseSqsWorkers(
                    o =>
                    {
                        o.QueueUrl = "https://sqs.test/second";
                        o.ConfigureSqsClient = c =>
                        {
                            c.RegionEndpoint = Amazon.RegionEndpoint.USEast1;
                            configured.Add("second");
                        };
                    },
                    routing => routing.ForTrain<ISecondTrain>()
                )
        );

        var routing = provider.GetRequiredService<JobSubmitterRoutingConfiguration>();
        routing
            .GetRegistration(typeof(IFirstTrain).FullName!)!
            .Description.Should()
            .Contain("first");
        routing
            .GetRegistration(typeof(ISecondTrain).FullName!)!
            .Description.Should()
            .Contain("second");

        using var scope = provider.CreateScope();
        routing
            .ResolveSubmitter(scope.ServiceProvider, typeof(IFirstTrain).FullName!)
            .Should()
            .BeOfType<SqsJobSubmitter>();
        routing
            .ResolveSubmitter(scope.ServiceProvider, typeof(ISecondTrain).FullName!)
            .Should()
            .BeOfType<SqsJobSubmitter>();

        configured
            .Should()
            .BeEquivalentTo(["first", "second"], "each registration builds its own client");
    }

    private static ServiceProvider BuildProvider(
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(configure)
        );
        return services.BuildServiceProvider();
    }

    private interface IFirstTrain;

    private interface ISecondTrain;
}
