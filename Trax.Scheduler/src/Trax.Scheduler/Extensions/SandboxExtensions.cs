using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Registers the two ends of a sandboxed run that live in a Trax host: the hub's client of the
/// sandbox launcher, and the entry point that runs inside a sandbox.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
public static class SandboxExtensions
{
    /// <summary>
    /// Registers <see cref="ITraxSandbox"/>, which runs a <see cref="TraxSandboxAttribute"/> train
    /// in a fresh sandbox through the launcher <paramref name="configure"/> points at.
    /// </summary>
    /// <param name="services">The hub's services.</param>
    /// <param name="configure">The launcher's address and the key shared with it.</param>
    /// <exception cref="InvalidOperationException">The options name no launcher, two, or no key; or a cleartext URL off loopback.</exception>
    public static IServiceCollection AddTraxSandbox(
        this IServiceCollection services,
        Action<SandboxClientOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new SandboxClientOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(TimeProvider.System);
        services
            .AddHttpClient(
                nameof(TraxSandboxClient),
                client =>
                {
                    client.BaseAddress = new Uri(options.LauncherUrl ?? "http://localhost");
                    // Each request sets its own deadline from the train's timeout.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() => Handler(options));

        services.AddScoped<ITraxSandbox>(sp => new TraxSandboxClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TraxSandboxClient)),
            options,
            sp.GetRequiredService<ITrainDiscoveryService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<TraxSandboxClient>>()
        ));
        return services;
    }

    /// <summary>
    /// Registers what a sandbox image needs to run a <see cref="TraxSandboxAttribute"/> train:
    /// the <see cref="SandboxWorkspace"/> its junctions inject, and the entry point
    /// <see cref="RunTraxSandboxAsync"/> calls. The image's host also registers Trax with an
    /// in-memory data provider and the mediator, and no database, broker or cloud credential.
    /// </summary>
    /// <param name="services">The sandbox image's services.</param>
    /// <param name="root">The sandbox root; the launcher mounts <see cref="SandboxPaths.Root"/>.</param>
    /// <param name="scratch">The scratch directory; the launcher mounts <see cref="SandboxPaths.Scratch"/>.</param>
    public static IServiceCollection AddTraxSandboxRunner(
        this IServiceCollection services,
        string root = SandboxPaths.Root,
        string scratch = SandboxPaths.Scratch
    )
    {
        services.AddSingleton(new SandboxWorkspace(root, scratch));
        services.AddSingleton(sp => new TraxSandboxEntryPoint(
            sp,
            sp.GetRequiredService<SandboxWorkspace>(),
            sp.GetRequiredService<ILogger<TraxSandboxEntryPoint>>()
        ));
        return services;
    }

    /// <summary>
    /// Runs the request the launcher left in the sandbox, or its startup canary, writes the result
    /// to the output volume, and returns the process's exit code. Call it from the sandbox image's
    /// entry point in place of starting the host.
    /// </summary>
    /// <param name="services">The sandbox image's built services, with <see cref="AddTraxSandboxRunner"/>.</param>
    /// <param name="ct">Cancels the run.</param>
    /// <returns>0 when a result was written, the train's own failure included; 2 when the request could not be read.</returns>
    public static Task<int> RunTraxSandboxAsync(
        this IServiceProvider services,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<TraxSandboxEntryPoint>().RunAsync(ct);
    }

    private static SocketsHttpHandler Handler(SandboxClientOptions options)
    {
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
        if (options.LauncherSocketPath is { } path)
            handler.ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified
                );
                try
                {
                    await socket
                        .ConnectAsync(new UnixDomainSocketEndPoint(path), ct)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        return handler;
    }
}
