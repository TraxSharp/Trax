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
    public void UseRemoteWorkers_RoutesTheTrainToItsOwnClient()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o => o.BaseUrl = "https://test.example.com/trax/execute",
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — the registration's own client carries its base address
        FirstClient(provider)
            .BaseAddress.Should()
            .Be(new Uri("https://test.example.com/trax/execute"));
    }

    [Test]
    public void UseRemoteWorkers_RoutesTheTrainToAnHttpJobSubmitter()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o => o.BaseUrl = "https://test.example.com/trax/execute",
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — the routed train resolves to the registration's HttpJobSubmitter
        using var scope = provider.CreateScope();
        var submitter = scope
            .ServiceProvider.GetRequiredService<JobSubmitterRoutingConfiguration>()
            .ResolveSubmitter(scope.ServiceProvider, typeof(ITestRemoteTrain).FullName!);
        submitter.Should().BeOfType<HttpJobSubmitter>();
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
        FirstClient(provider).Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void UseRemoteWorkers_ConfigureHttpClientCallbackConfiguresItsClient()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(
                o =>
                {
                    o.BaseUrl = "https://test.example.com/trax/execute";
                    o.ConfigureHttpClient = client =>
                        client.DefaultRequestHeaders.Add("X-Custom", "test");
                },
                routing => routing.ForTrain<ITestRemoteTrain>()
            )
        );

        // Assert — the callback ran on the registration's client
        FirstClient(provider).DefaultRequestHeaders.GetValues("X-Custom").Should().Equal("test");
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

    #region Optional Routing Tests

    [Test]
    public void UseRemoteWorkers_WithoutRouting_RegistersItsClient()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(o => o.BaseUrl = "https://test.example.com/trax/execute")
        );

        // Assert
        FirstClient(provider)
            .BaseAddress.Should()
            .Be(new Uri("https://test.example.com/trax/execute"));
    }

    [Test]
    public void UseRemoteWorkers_WithoutRouting_RoutesNoTrain()
    {
        // Arrange & Act
        using var provider = BuildProvider(s =>
            s.UseRemoteWorkers(o => o.BaseUrl = "https://test.example.com/trax/execute")
        );

        // Assert — only [TraxRemote] trains would reach it, and ITestRemoteTrain is not one
        var routing = provider.GetRequiredService<JobSubmitterRoutingConfiguration>();
        routing.GetRegistration(typeof(ITestRemoteTrain).FullName!).Should().BeNull();
    }

    /// <summary>The HTTP client of the first <c>UseRemoteWorkers</c> registration.</summary>
    private static HttpClient FirstClient(ServiceProvider provider) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient("Trax.RemoteWorkers.0");

    #endregion
}

internal interface ITestRemoteTrain { }
