using Amazon.Lambda;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.RunExecutor;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Lambda.Extensions;
using Trax.Scheduler.Lambda.Services;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class UseLambdaWorkersBuilderTests
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

    #region UseLambdaWorkers Service Registration Tests

    [Test]
    public void UseLambdaWorkers_RegistersLambdaWorkerOptions()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseLambdaWorkers(
                o => o.FunctionName = "my-function",
                routing => routing.ForTrain<ITestLambdaTrain>()
            )
        );

        // Assert
        var options = provider.GetService<LambdaWorkerOptions>();
        options.Should().NotBeNull();
        options!.FunctionName.Should().Be("my-function");
    }

    [Test]
    public void UseLambdaWorkers_RegistersIAmazonLambda()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseLambdaWorkers(
                o =>
                {
                    o.FunctionName = "my-function";
                    o.ConfigureLambdaClient = cfg => cfg.ServiceURL = "http://localhost:3001";
                },
                routing => routing.ForTrain<ITestLambdaTrain>()
            )
        );

        // Assert
        var lambdaClient = provider.GetService<IAmazonLambda>();
        lambdaClient.Should().NotBeNull();
    }

    [Test]
    public void UseLambdaWorkers_RegistersLambdaJobSubmitter()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseLambdaWorkers(
                o =>
                {
                    o.FunctionName = "my-function";
                    o.ConfigureLambdaClient = cfg => cfg.ServiceURL = "http://localhost:3001";
                },
                routing => routing.ForTrain<ITestLambdaTrain>()
            )
        );

        // Assert — LambdaJobSubmitter is registered as a concrete type for routing
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<LambdaJobSubmitter>();
        submitter.Should().NotBeNull();
    }

    [Test]
    public void UseLambdaWorkers_DefaultSubmitterRemainsInMemory()
    {
        // Lambda workers are per-train routing — the default IJobSubmitter stays as InMemory
        using var provider = BuildProvider(s =>
            s.UseLambdaWorkers(
                o =>
                {
                    o.FunctionName = "my-function";
                    o.ConfigureLambdaClient = cfg => cfg.ServiceURL = "http://localhost:3001";
                },
                routing => routing.ForTrain<ITestLambdaTrain>()
            )
        );

        // Assert — default IJobSubmitter is still InMemoryJobSubmitter
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().BeOfType<InMemoryJobSubmitter>();
    }

    [Test]
    public void UseLambdaWorkers_ConfigureLambdaClientCallbackIsStored()
    {
        // Arrange
        var callbackInvoked = false;

        // Act
        using var provider = BuildProvider(s =>
            s.UseLambdaWorkers(
                o =>
                {
                    o.FunctionName = "my-function";
                    o.ConfigureLambdaClient = _ =>
                    {
                        callbackInvoked = true;
                    };
                },
                routing => routing.ForTrain<ITestLambdaTrain>()
            )
        );

        // Assert
        var options = provider.GetRequiredService<LambdaWorkerOptions>();
        options.ConfigureLambdaClient.Should().NotBeNull();

        options.ConfigureLambdaClient!(new Amazon.Lambda.AmazonLambdaConfig());
        callbackInvoked.Should().BeTrue();
    }

    #endregion

    #region UseLambdaRun Tests

    [Test]
    public void UseLambdaRun_RegistersLambdaRunExecutor()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseLambdaRun(o =>
            {
                o.FunctionName = "my-runner";
                o.ConfigureLambdaClient = cfg => cfg.ServiceURL = "http://localhost:3001";
            })
        );

        // Assert
        using var scope = provider.CreateScope();
        var executor = scope.ServiceProvider.GetService<IRunExecutor>();
        executor.Should().BeOfType<LambdaRunExecutor>();
    }

    [Test]
    public void UseLambdaRun_RegistersLambdaRunOptions()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseLambdaRun(o =>
            {
                o.FunctionName = "my-runner";
                o.ConfigureLambdaClient = cfg => cfg.ServiceURL = "http://localhost:3001";
            })
        );

        // Assert
        var options = provider.GetService<LambdaRunOptions>();
        options.Should().NotBeNull();
        options!.FunctionName.Should().Be("my-runner");
    }

    #endregion
}

internal interface ITestLambdaTrain { }
