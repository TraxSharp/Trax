using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// Starts a host the way the generic host does, running every hosted service's <c>StartingAsync</c> and then its
/// <c>StartAsync</c>, over <c>AddStateMachines</c> with only the machines a test names registered, and stops it
/// again. A startup refusal is returned rather than thrown, so a test can read it.
/// </summary>
internal static class StartingHost
{
    // Long enough never to be reached by a host that starts, so it only names a hang.
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    /// <summary>The exception starting the host threw, or <c>null</c> when it started.</summary>
    /// <param name="configure">The state-machine options, such as the generic opt-in.</param>
    /// <param name="machines">The only machines the host registers.</param>
    public static Task<Exception?> Start(
        Action<StateMachineOptions> configure,
        params IMachine[] machines
    ) =>
        Start(
            configure,
            typeof(OrderMachine).Assembly,
            services =>
            {
                // The scan registers every fake in this assembly; the test is about the ones it names.
                services.RemoveAll<IMachine>();
                foreach (var machine in machines)
                    services.AddSingleton(machine);
            }
        );

    /// <summary>
    /// The exception starting the host threw, or <c>null</c> when it started, with the machines
    /// <c>AddStateMachines</c> discovers in <paramref name="assembly"/> registered as it registers them.
    /// </summary>
    /// <param name="configure">The state-machine options.</param>
    /// <param name="assembly">The only assembly the host scans for machines.</param>
    public static Task<Exception?> StartScanning(
        Action<StateMachineOptions> configure,
        Assembly assembly
    ) => Start(configure, assembly, _ => { });

    private static async Task<Exception?> Start(
        Action<StateMachineOptions> configure,
        Assembly assembly,
        Action<IServiceCollection> registered
    )
    {
        var file = Path.Combine(Path.GetTempPath(), $"sm_startup_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseSqlite($"Data Source={file}"))
                .AddStateMachines(configure, assembly)
        );

        registered(services);
        services.AddSingleton<IInvokedTrainLauncher>(new RecordingLauncher());

        using var timeout = new CancellationTokenSource(Hang);
        var provider = services.BuildServiceProvider();
        var started = new List<IHostedService>();
        try
        {
            var hosted = provider.GetServices<IHostedService>().ToList();
            foreach (var service in hosted.OfType<IHostedLifecycleService>())
                await service.StartingAsync(timeout.Token);
            foreach (var service in hosted)
            {
                await service.StartAsync(timeout.Token);
                started.Add(service);
            }
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex;
        }
        finally
        {
            foreach (var service in started)
                await service.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
            }
        }
    }
}
