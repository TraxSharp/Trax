using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class UseRemoteWorkersBuilderTests
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
    public void UseRemoteWorkers_RegistersRemoteWorkerOptions()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o => o.BaseUrl = "https://test.example.com/trax/execute",
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert
        var options = provider.GetService<RemoteWorkerOptions>();
        options.Should().NotBeNull();
        options!.BaseUrl.Should().Be("https://test.example.com/trax/execute");
    }

    [Test]
    public void UseRemoteWorkers_RegistersHttpJobSubmitter()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o => o.BaseUrl = "https://test.example.com/trax/execute",
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — HttpJobSubmitter is registered as a concrete type for routing
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<HttpJobSubmitter>();
        submitter.Should().NotBeNull();
    }

    [Test]
    public void UseRemoteWorkers_ConfiguresTimeout()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o =>
                {
                    o.BaseUrl = "https://test.example.com/trax/execute";
                    o.Timeout = TimeSpan.FromMinutes(5);
                },
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert
        var options = provider.GetRequiredService<RemoteWorkerOptions>();
        options.Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void UseRemoteWorkers_ConfigureHttpClientCallbackIsStored()
    {
        // Arrange
        var headerAdded = false;

        // Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o =>
                {
                    o.BaseUrl = "https://test.example.com/trax/execute";
                    o.ConfigureHttpClient = client =>
                    {
                        client.DefaultRequestHeaders.Add("X-Custom", "test");
                        headerAdded = true;
                    };
                },
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — the callback is stored in options
        var options = provider.GetRequiredService<RemoteWorkerOptions>();
        options.ConfigureHttpClient.Should().NotBeNull();

        // Invoke the callback to verify it works
        options.ConfigureHttpClient!(new HttpClient());
        headerAdded.Should().BeTrue();
    }

    #endregion

    #region Does Not Register Local Worker Tests

    [Test]
    public void UseRemoteWorkers_DoesNotRegisterLocalWorkerOptions()
    {
        // Arrange & Act — InMemory provider does not register LocalWorkerOptions
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o => o.BaseUrl = "https://test.example.com/trax/execute",
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — LocalWorkerOptions should not be registered (InMemory provider)
        var localOptions = provider.GetService<LocalWorkerOptions>();
        localOptions.Should().BeNull();
    }

    #endregion

    #region Routing Configuration Tests

    [Test]
    public void UseRemoteWorkers_DefaultSubmitterRemainsInMemory()
    {
        // Remote workers are per-train routing — the default IJobSubmitter stays as InMemory
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o => o.BaseUrl = "https://test.example.com/trax/execute",
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — default IJobSubmitter is still InMemoryJobSubmitter
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().BeOfType<InMemoryJobSubmitter>();
    }

    #endregion
}

internal interface ITestRemoteTrain { }
