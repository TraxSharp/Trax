using System.Net;
using System.Net.Sockets;
using k8s;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Sandbox.Launcher.Core;

namespace Trax.Scheduler.Sandbox.Launcher;

/// <summary>
/// The sandbox launcher as a process: the only thing that can start sandboxes. Call
/// <see cref="RunAsync"/> from the launcher's <c>Program.cs</c>; it reads
/// <see cref="SandboxLauncherOptions"/> from configuration, refuses to start on any problem with
/// them, sweeps what an earlier process left, runs the runtime canary, and only then listens.
/// </summary>
/// <example>
/// <code>
/// await Trax.Scheduler.Sandbox.Launcher.TraxSandboxLauncher.RunAsync(args);
/// </code>
/// </example>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public static class TraxSandboxLauncher
{
    /// <summary>Builds and runs the launcher until <paramref name="ct"/> or a shutdown signal.</summary>
    /// <param name="args">The process's arguments, read as configuration.</param>
    /// <param name="ct">Stops the launcher.</param>
    /// <exception cref="InvalidOperationException">The configuration is missing or unsafe.</exception>
    public static async Task RunAsync(string[] args, CancellationToken ct = default)
    {
        var app = Build(args);
        await using (app.ConfigureAwait(false))
            await app.RunAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Builds the launcher without running it, for a host that adds its own logging or health checks first.</summary>
    /// <param name="args">The process's arguments, read as configuration.</param>
    /// <exception cref="InvalidOperationException">The configuration is missing or unsafe.</exception>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);
        var options = SandboxLauncherOptions.Read(builder.Configuration);
        options.Validate();
        return Build(builder, options, BackendFor, RuntimeCanary.ReadHostKernelAsync);
    }

    /// <summary>The launcher over a given backend and kernel reader; the tests' way in.</summary>
    internal static WebApplication Build(
        WebApplicationBuilder builder,
        SandboxLauncherOptions options,
        Func<IServiceProvider, SandboxLauncherOptions, ISandboxBackend> backend,
        Func<CancellationToken, Task<string>> hostKernel
    )
    {
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => backend(sp, options));
        builder.Services.AddSingleton(new SandboxGate(options.Caps.MaxConcurrent));
        builder.Services.AddSingleton<NonceCache>();
        builder.Services.AddSingleton<SandboxRunner>();
        builder.Services.AddSingleton<LauncherEndpoint>();
        builder.Services.AddSingleton(sp => new RuntimeCanary(
            sp.GetRequiredService<ISandboxBackend>(),
            options,
            hostKernel,
            sp.GetRequiredService<ILogger<RuntimeCanary>>()
        ));
        builder.Services.AddHostedService<LauncherStartup>();

        var maxBody = (long)options.Caps.MaxInputBytes * 6 + 64 * 1024;
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = maxBody;
            kestrel.AddServerHeader = false;
            if (options.ListenSocketPath is { } socket)
            {
                RemoveStaleSocket(socket);
                kestrel.ListenUnixSocket(socket);
            }
            else
            {
                var url = new Uri(options.ListenUrl ?? "");
                kestrel.Listen(IPAddress.Parse(url.Host.Trim('[', ']')), url.Port);
            }
        });

        var app = builder.Build();
        if (options.ListenSocketPath is { } path)
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, options.ListenSocketMode);
            });

        app.MapPost(
            LauncherEndpoint.Route,
            (HttpContext context, LauncherEndpoint endpoint) => endpoint.HandleAsync(context)
        );
        app.MapGet("/healthz", () => Results.Text("ok"));
        return app;
    }

    /// <summary>
    /// Removes a socket an earlier launcher left, so a restart can listen again. Only an empty
    /// file named <c>*.sock</c> is removed: a misconfigured path to anything else is left alone,
    /// and listening on it then fails.
    /// </summary>
    internal static void RemoveStaleSocket(string path)
    {
        var file = new FileInfo(path);
        if (file.Exists && file.Length == 0 && path.EndsWith(".sock", StringComparison.Ordinal))
            file.Delete();
    }

    private static ISandboxBackend BackendFor(
        IServiceProvider services,
        SandboxLauncherOptions options
    ) =>
        options.Backend switch
        {
            SandboxBackendKind.Podman => new PodmanSandboxBackend(
                new PodmanApi(PodmanClient(options.Podman.SocketPath)),
                options,
                PodmanSandboxBackend.RuntimeName(options.Runtime),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<PodmanSandboxBackend>>()
            ),
            SandboxBackendKind.Kubernetes => new KubernetesSandboxBackend(
                new Kubernetes(KubernetesClientConfiguration.InClusterConfig()),
                options,
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ILogger<KubernetesSandboxBackend>>()
            ),
            _ => throw new InvalidOperationException($"No backend {options.Backend}."),
        };

    /// <summary>An HTTP client over podman's Unix socket.</summary>
    internal static HttpClient PodmanClient(string socketPath)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(
                    AddressFamily.Unix,
                    SocketType.Stream,
                    ProtocolType.Unspecified
                );
                try
                {
                    await socket
                        .ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct)
                        .ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://podman"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
