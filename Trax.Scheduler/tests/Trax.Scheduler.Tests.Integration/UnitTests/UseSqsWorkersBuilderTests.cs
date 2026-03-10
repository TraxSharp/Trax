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
            s.UseSqsWorkers(o =>
                o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs"
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
            s.UseSqsWorkers(o =>
            {
                o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                o.ConfigureSqsClient = cfg => cfg.ServiceURL = "http://localhost:4566";
            })
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
            s.UseSqsWorkers(o =>
            {
                o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                o.ConfigureSqsClient = cfg => cfg.ServiceURL = "http://localhost:4566";
            })
        );

        // Assert
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().NotBeNull();
        submitter.Should().BeOfType<SqsJobSubmitter>();
    }

    [Test]
    public void UseSqsWorkers_DoesNotRegisterLocalWorkerOptions()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseSqsWorkers(o =>
                o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs"
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
            s.UseSqsWorkers(o =>
            {
                o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                o.ConfigureSqsClient = _ =>
                {
                    callbackInvoked = true;
                };
            })
        );

        // Assert
        var options = provider.GetRequiredService<SqsWorkerOptions>();
        options.ConfigureSqsClient.Should().NotBeNull();

        options.ConfigureSqsClient!(new Amazon.SQS.AmazonSQSConfig());
        callbackInvoked.Should().BeTrue();
    }

    #endregion

    #region Overrides Previous Registration Tests

    [Test]
    public void UseSqsWorkers_OverridesUseInMemoryWorkers()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
        {
            s.UseInMemoryWorkers();
            s.UseSqsWorkers(o =>
            {
                o.QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789/trax-jobs";
                o.ConfigureSqsClient = cfg => cfg.ServiceURL = "http://localhost:4566";
            });
        });

        // Assert
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().BeOfType<SqsJobSubmitter>();
    }

    #endregion
}
