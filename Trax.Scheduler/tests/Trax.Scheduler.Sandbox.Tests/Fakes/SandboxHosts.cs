using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Sandbox.Image;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>Builds the hosts the tests use: a hub with the escape image's trains, and the image's own host.</summary>
public static class SandboxHosts
{
    public static readonly byte[] Key = Enumerable.Range(7, 32).Select(i => (byte)i).ToArray();

    /// <summary>A Trax host with the escape image's trains registered, as both a hub and the image are.</summary>
    public static ServiceProvider Trains(Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(HostileTrain).Assembly)
            );
        more?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The hub's client over <paramref name="launcher"/>.</summary>
    internal static TraxSandboxClient Client(
        ServiceProvider hub,
        HttpMessageHandler launcher,
        SandboxClientOptions? options = null
    ) =>
        new(
            new HttpClient(launcher) { BaseAddress = new Uri("http://launcher") },
            options ?? Options(),
            hub.GetRequiredService<ITrainDiscoveryService>(),
            TimeProvider.System,
            NullLogger<TraxSandboxClient>.Instance
        );

    public static SandboxClientOptions Options() =>
        new() { LauncherUrl = "http://localhost:7300", SigningKey = Key };

    /// <summary>The image's host, rooted in <paramref name="root"/>.</summary>
    public static ServiceProvider Image(string root) =>
        Trains(services => services.AddTraxSandboxRunner(root, Path.Combine(root, "scratch")));
}
