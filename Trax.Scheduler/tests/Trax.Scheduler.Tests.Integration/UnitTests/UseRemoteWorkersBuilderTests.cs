using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
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
            s.UseRemoteWorkers(o => o.BaseUrl = "https://test.example.com/trax/execute")
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
            s.UseRemoteWorkers(o => o.BaseUrl = "https://test.example.com/trax/execute")
        );

        // Assert
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().NotBeNull();
        submitter.Should().BeOfType<HttpJobSubmitter>();
    }

    [Test]
    public void UseRemoteWorkers_ConfiguresTimeout()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(o =>
            {
                o.BaseUrl = "https://test.example.com/trax/execute";
                o.Timeout = TimeSpan.FromMinutes(5);
            })
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
            s.UseRemoteWorkers(o =>
            {
                o.BaseUrl = "https://test.example.com/trax/execute";
                o.ConfigureHttpClient = client =>
                {
                    client.DefaultRequestHeaders.Add("X-Custom", "test");
                    headerAdded = true;
                };
            })
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
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(o => o.BaseUrl = "https://test.example.com/trax/execute")
        );

        // Assert — LocalWorkerOptions should not be registered
        var localOptions = provider.GetService<LocalWorkerOptions>();
        localOptions.Should().BeNull();
    }

    #endregion

    #region Overrides Previous Registration Tests

    [Test]
    public void UseRemoteWorkers_OverridesUseInMemoryWorkers()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
        {
            s.UseInMemoryWorkers(); // First register in-memory
            s.UseRemoteWorkers(o => o.BaseUrl = "https://test.example.com/trax/execute"); // Then override with remote
        });

        // Assert
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().BeOfType<HttpJobSubmitter>();
    }

    #endregion
}
