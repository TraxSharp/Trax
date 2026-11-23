using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.RunStarters;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// Enforces mediator/0006: <c>AddMediator</c> on effects with no data provider registers the bus
/// and leaves out the services that read or write the run-record tables, so a host that only runs
/// trains in process passes container validation and starts. With a data provider, they are all
/// registered.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0006-without-a-data-provider-the-mediator-only-runs-trains.md")]
public class MediatorWithoutDataProviderTests
{
    private const string Adr =
        "(docs/adr/0006-without-a-data-provider-the-mediator-only-runs-trains.md)";

    // IInvokedTrainLauncher is internal to Trax.StateMachine.Persistence, so it is named here.
    private static readonly string[] RunRecordingServices =
    [
        $"{typeof(ITrainExecutionService).Namespace}.{nameof(ITrainExecutionService)}",
        $"{typeof(IRunStarters).Namespace}.{nameof(IRunStarters)}",
        $"{typeof(IRunExecutor).Namespace}.{nameof(IRunExecutor)}",
        $"{typeof(IWorkQueuePromotion).Namespace}.{nameof(IWorkQueuePromotion)}",
        "Trax.StateMachine.Persistence.IInvokedTrainLauncher",
    ];

    private static ServiceCollection MediatorHost(bool withDataProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            (
                withDataProvider
                    ? trax.AddEffects(effects => effects.UseInMemory())
                    : trax.AddEffects()
            ).AddMediator(mediator =>
                // Trax.Mediator declares no trains, so the scan contributes none.
                mediator.ScanAssemblies(typeof(ITrainBus).Assembly)
            )
        );
        return services;
    }

    [Test]
    public async Task WithoutADataProvider_TheHostValidatesAndStarts()
    {
        var services = MediatorHost(withDataProvider: false);

        var build = () =>
            services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );

        build
            .Should()
            .NotThrow(
                $"a host with no data provider never queues a train, so AddMediator must not register "
                    + $"services that need one, or Development's ValidateOnBuild refuses it {Adr}"
            );

        var provider = build();
        await using var providerDisposal = provider.ConfigureAwait(false);

        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            if (hosted is IHostedLifecycleService lifecycle)
                await lifecycle.StartingAsync(CancellationToken.None);
            await hosted.StartAsync(CancellationToken.None);
        }

        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        scope
            .ServiceProvider.GetService<ITrainBus>()
            .Should()
            .NotBeNull($"trains still run in process without a data provider {Adr}");
    }

    [Test]
    public void WithoutADataProvider_NoRunRecordingServiceIsRegistered()
    {
        var services = MediatorHost(withDataProvider: false);

        services
            .Select(descriptor => descriptor.ServiceType.FullName)
            .Intersect(RunRecordingServices, StringComparer.Ordinal)
            .Should()
            .BeEmpty(
                $"each of these needs IDataContextProviderFactory, which only a data provider "
                    + $"registers {Adr}"
            );
    }

    [Test]
    public void WithADataProvider_EveryRunRecordingServiceIsRegistered()
    {
        var services = MediatorHost(withDataProvider: true);

        services
            .Select(descriptor => descriptor.ServiceType.FullName)
            .Should()
            .Contain(
                RunRecordingServices,
                $"with a data provider, AddMediator registers the execution service and what it "
                    + $"needs {Adr}"
            );
    }
}
