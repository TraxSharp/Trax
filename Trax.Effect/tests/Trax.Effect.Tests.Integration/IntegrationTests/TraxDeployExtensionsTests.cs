using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DeployMigration;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>RunTraxAsync</c> reads the deploy flags from the arguments it is handed and from the process's own, the way
/// registration reads them, so a host handed no arguments still migrates rather than serving into its own gate.
/// </summary>
[TestFixture]
public class TraxDeployExtensionsTests
{
    [Test]
    public async Task RunTraxAsync_honours_the_process_flag_when_given_no_args()
    {
        var migration = new RecordingMigration();
        var services = new ServiceCollection();
        services.AddTrax(
            trax => trax.AddEffects(effects => effects.UseInMemory()),
            exportsDeploy: false,
            migrates: true,
            exportsClient: false
        );
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDeployMigration>(migration));
        await using var provider = services.BuildServiceProvider();
        var host = new GateStartingHost(provider);

        var run = async () =>
            await host.RunTraxAsync(
                [],
                processExportPath: null,
                processMigrates: true,
                CancellationToken.None
            );

        await run.Should().NotThrowAsync();
        migration.Runs.Should().Be(1);
        host.Started.Should().BeFalse();
    }

    private sealed class RecordingMigration : IDeployMigration
    {
        public int Runs { get; private set; }

        public string ContextName => "Recording";

        public Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult("recorded");
        }
    }

    /// <summary>A host that starts its hosted services as the generic host does, so a serve gate refuses it.</summary>
    private sealed class GateStartingHost(IServiceProvider services) : IHost
    {
        public bool Started { get; private set; }

        public IServiceProvider Services { get; } = services;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            Started = true;
            foreach (var hosted in Services.GetServices<IHostedService>())
            {
                if (hosted is IHostedLifecycleService lifecycle)
                    await lifecycle.StartingAsync(cancellationToken);
                await hosted.StartAsync(cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose() { }
    }
}
