using Amazon.SQS;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Sqs.Configuration;
using Trax.Scheduler.Sqs.Extensions;
using Trax.Scheduler.Sqs.Services;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class UseSqsWorkersBuilderTests
{
    private ServiceProvider BuildProvider(Action<SchedulerConfigurationBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                {
                    configure(scheduler);
                    return scheduler;
                })
        );
        return services.BuildServiceProvider();
    }

    #region Service Registration Tests

    [Test]
    public void UseSqsWorkers_RegistersSqsWorkerOptions()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(
                o => o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs",
                routing => routing.ForTrain<ITestSqsTrain>()
            )
        );

        // Assert
        var options = provider.GetService<SqsWorkerOptions>();
        options.Should().NotBeNull();
        options!.QueueUrl.Should().Be("https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs");
    }

    [Test]
    public void UseSqsWorkers_RegistersIAmazonSQS()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(
                o =>
                {
                    o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                    o.ConfigureSqsClient = cfg => cfg.ServiceURL = "http://localhost:4566";
                },
                routing => routing.ForTrain<ITestSqsTrain>()
            )
        );

        // Assert
        var sqsClient = provider.GetService<IAmazonSQS>();
        sqsClient.Should().NotBeNull();
    }

    [Test]
    public void UseSqsWorkers_RegistersSqsJobSubmitter()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(
                o =>
                {
                    o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                    o.ConfigureSqsClient = cfg => cfg.ServiceURL = "http://localhost:4566";
                },
                routing => routing.ForTrain<ITestSqsTrain>()
            )
        );

        // Assert — SqsJobSubmitter is registered as a concrete type for routing
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<SqsJobSubmitter>();
        submitter.Should().NotBeNull();
    }

    [Test]
    public void UseSqsWorkers_DoesNotRegisterLocalWorkerOptions()
    {
        // Arrange & Act — InMemory provider does not register LocalWorkerOptions
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(
                o => o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs",
                routing => routing.ForTrain<ITestSqsTrain>()
            )
        );

        // Assert
        var localOptions = provider.GetService<LocalWorkerOptions>();
        localOptions.Should().BeNull();
    }

    [Test]
    public void UseSqsWorkers_ConfigureSqsClientCallbackIsStored()
    {
        // Arrange
        var callbackInvoked = false;

        // Act
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(
                o =>
                {
                    o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                    o.ConfigureSqsClient = _ =>
                    {
                        callbackInvoked = true;
                    };
                },
                routing => routing.ForTrain<ITestSqsTrain>()
            )
        );

        // Assert
        var options = provider.GetRequiredService<SqsWorkerOptions>();
        options.ConfigureSqsClient.Should().NotBeNull();

        options.ConfigureSqsClient!(new Amazon.SQS.AmazonSQSConfig());
        callbackInvoked.Should().BeTrue();
    }

    #endregion

    #region Routing Configuration Tests

    [Test]
    public void UseSqsWorkers_DefaultSubmitterRemainsInMemory()
    {
        // SQS workers are per-train routing — the default IJobSubmitter stays as InMemory
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(
                o =>
                {
                    o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                    o.ConfigureSqsClient = cfg => cfg.ServiceURL = "http://localhost:4566";
                },
                routing => routing.ForTrain<ITestSqsTrain>()
            )
        );

        // Assert — default IJobSubmitter is still InMemoryJobSubmitter
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().BeOfType<InMemoryJobSubmitter>();
    }

    #endregion
}

internal interface ITestSqsTrain { }
