using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

/// <summary>
/// The startup gates refuse a real host before any other hosted service starts, whether the host
/// starts its services one after another or all at once.
/// </summary>
/// <remarks>
/// A gate that ran as an ordinary <c>StartAsync</c> only ran first because it was registered
/// first. Under <c>HostOptions.ServicesStartConcurrently</c> every <c>StartAsync</c> began at once,
/// so a worker the host registered before calling into Trax started claiming work alongside the
/// check that was about to refuse the host. The gates check in <c>StartingAsync</c>, which the host
/// finishes for every service before it calls any <c>StartAsync</c>.
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md
/// and docs/adr/0001-authorization-is-fail-closed.md.</para>
/// </remarks>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
[Property("adr", "docs/adr/0001-authorization-is-fail-closed.md")]
[TestFixture]
public class StartupGateHostTests
{
    /// <summary>
    /// Builds a host whose own worker is registered before Trax, then registers
    /// <paramref name="registerTrains"/> alone, so the gates judge exactly those trains.
    /// </summary>
    private static IHost BuildHost(
        bool concurrent,
        MarkerWorker worker,
        Action<IServiceCollection> registerTrains,
        bool skipChainVerification = false
    )
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );

        builder.Services.Configure<HostOptions>(options =>
            options.ServicesStartConcurrently = concurrent
        );

        // The host's own worker goes in before Trax is configured at all.
        builder.Services.AddSingleton<IHostedService>(worker);

        builder.Services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(mediator =>
                {
                    // Trax.Mediator's own assembly holds no trains, so the gates see only the
                    // ones registered below.
                    mediator.ScanAssemblies(typeof(ITrainBus).Assembly);
                    return skipChainVerification ? mediator.SkipChainVerification() : mediator;
                })
        );

        registerTrains(builder.Services);

        return builder.Build();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AHostWithAChainThatCannotRun_RefusesToStart_BeforeItsWorkerStarts(
        bool concurrent
    )
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent,
            worker,
            services =>
                services.AddScopedTraxRoute<
                    TrainChainStartupValidatorTests.IBrokenFlowTrain,
                    TrainChainStartupValidatorTests.BrokenFlowTrain
                >()
        );

        var start = async () => await host.StartAsync();

        await start
            .Should()
            .ThrowAsync<Exception>()
            .Where(ex =>
                ex.ToString().Contains(nameof(TrainChainStartupValidatorTests.IBrokenFlowTrain))
            );
        worker
            .Started.Should()
            .BeFalse(
                "a gate that only sometimes runs before the work it gates is not a gate, and with "
                    + "ServicesStartConcurrently every StartAsync begins at once"
            );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AHostWithAGatedTrainAndNoEnforcer_RefusesToStart_BeforeItsWorkerStarts(
        bool concurrent
    )
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent,
            worker,
            services =>
                services.AddScopedTraxRoute<
                    AuthorizationRegistrationValidatorTests.ITestAuthedTrain,
                    AuthorizationRegistrationValidatorTests.TestAuthedTrain
                >(),
            skipChainVerification: true
        );

        var start = async () => await host.StartAsync();

        await start
            .Should()
            .ThrowAsync<Exception>()
            .Where(ex => ex.ToString().Contains("no ITrainAuthorizationService is registered"));
        worker.Started.Should().BeFalse("authorization is fail-closed at startup too");
    }

    [Test]
    public async Task AHostWithAJunctionThatHasTwoConstructors_RefusesToStartNamingIt()
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent: false,
            worker,
            services =>
                services.AddScopedTraxRoute<
                    TrainChainStartupValidatorTests.ITwoConstructorJunctionTrain,
                    TrainChainStartupValidatorTests.TwoConstructorJunctionTrain
                >()
        );

        var start = async () => await host.StartAsync();

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain(nameof(TrainChainStartupValidatorTests.ITwoConstructorJunctionTrain))
            .And.Contain(
                nameof(TrainChainStartupValidatorTests.TwoConstructorJunction),
                "Trax builds a junction through its single constructor, so one it cannot build "
                    + "fails every run of the train"
            );
        worker.Started.Should().BeFalse();
    }

    [Test]
    public async Task AHostWithATrainNeedingAnUnregisteredService_RefusesToStartNamingIt()
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent: false,
            worker,
            services =>
                services.AddScopedTraxRoute<
                    TrainChainStartupValidatorTests.INeedsUnregisteredTrain,
                    TrainChainStartupValidatorTests.NeedsUnregisteredTrain
                >()
        );

        var start = async () => await host.StartAsync();

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain(nameof(TrainChainStartupValidatorTests.INeedsUnregisteredTrain))
            .And.Contain(nameof(TrainChainStartupValidatorTests.IUnregisteredProbeService))
            .And.Contain("Register it");
        worker.Started.Should().BeFalse();
    }

    [Test]
    public async Task AHostWithATrainWhoseDependencyNeedsAnUnregisteredService_RefusesToStartNamingThePath()
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent: false,
            worker,
            services =>
            {
                services.AddScoped<
                    TrainChainStartupValidatorTests.IProbeRepository,
                    TrainChainStartupValidatorTests.ProbeRepository
                >();
                services.AddScopedTraxRoute<
                    TrainChainStartupValidatorTests.INeedsNestedUnregisteredTrain,
                    TrainChainStartupValidatorTests.NeedsNestedUnregisteredTrain
                >();
            }
        );

        var start = async () => await host.StartAsync();

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain(nameof(TrainChainStartupValidatorTests.INeedsNestedUnregisteredTrain))
            .And.Contain(nameof(TrainChainStartupValidatorTests.IUnregisteredProbeService))
            .And.Contain(
                nameof(TrainChainStartupValidatorTests.IProbeRepository),
                "the reader has to know which registered dependency leads to the missing type"
            );
        worker.Started.Should().BeFalse();
    }

    [Test]
    public async Task AHarnessThatCallsOnlyStartAsync_IsRefusedAGatedTrainWithNoEnforcer()
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent: false,
            worker,
            services =>
                services.AddScopedTraxRoute<
                    AuthorizationRegistrationValidatorTests.ITestAuthedTrain,
                    AuthorizationRegistrationValidatorTests.TestAuthedTrain
                >(),
            skipChainVerification: true
        );

        // What a custom IHost or a test harness does: start each hosted service directly, with no
        // StartingAsync before it.
        var start = async () =>
        {
            foreach (var service in host.Services.GetServices<IHostedService>())
                await service.StartAsync(CancellationToken.None);
        };

        await start
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("no ITrainAuthorizationService is registered"));
    }

    [Test]
    public async Task AHostWhoseTrainsAllPass_Starts()
    {
        var worker = new MarkerWorker();
        using var host = BuildHost(
            concurrent: true,
            worker,
            services =>
                services.AddScopedTraxRoute<
                    TrainChainStartupValidatorTests.IWellFormedTrain,
                    TrainChainStartupValidatorTests.WellFormedTrain
                >()
        );

        await host.StartAsync();
        worker.Started.Should().BeTrue();
        await host.StopAsync();
    }

    public sealed class MarkerWorker : IHostedService
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
