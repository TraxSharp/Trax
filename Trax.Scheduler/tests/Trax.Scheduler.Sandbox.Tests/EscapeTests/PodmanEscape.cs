using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Sandbox.Launcher;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Sandbox.Launcher.Core;
using Trax.Scheduler.Sandbox.Tests.Fakes;

namespace Trax.Scheduler.Sandbox.Tests.EscapeTests;

/// <summary>
/// A real launcher over a real podman, for the escape suite. Read from the environment, since
/// trax-test has no podman socket to give it and CI's <c>sandbox-escape</c> job starts its own:
/// <c>TRAX_SANDBOX_PODMAN_SOCKET</c>, <c>TRAX_SANDBOX_IMAGE</c> and <c>TRAX_SANDBOX_HOLDER_IMAGE</c>
/// (both pinned by digest; <c>build-escape-image.sh</c> prints them), and <c>TRAX_SANDBOX_OCI_RUNTIME</c>
/// (<c>kata</c> unless set). With <c>TRAX_SANDBOX_ASSUME_ISOLATED=1</c> the launcher takes any kernel
/// as a guest's, which is how the mechanics are proved under crun on a host without Kata; it is
/// a test seam only, never an option of the launcher. With <c>TRAX_SANDBOX_ESCAPE_REQUIRED=1</c>, as
/// CI sets it, a missing socket or image fails the suite instead of skipping it.
/// </summary>
internal sealed class PodmanEscape : IAsyncDisposable
{
    public static string? Socket =>
        Environment.GetEnvironmentVariable("TRAX_SANDBOX_PODMAN_SOCKET");
    public static string Image => Environment.GetEnvironmentVariable("TRAX_SANDBOX_IMAGE") ?? "";
    public static string Holder =>
        Environment.GetEnvironmentVariable("TRAX_SANDBOX_HOLDER_IMAGE") ?? "";
    public static string Runtime =>
        Environment.GetEnvironmentVariable("TRAX_SANDBOX_OCI_RUNTIME") ?? "kata";
    public static bool AssumeIsolated =>
        string.Equals(
            Environment.GetEnvironmentVariable("TRAX_SANDBOX_ASSUME_ISOLATED"),
            "1",
            StringComparison.Ordinal
        );

    private readonly WebApplication _app;

    private PodmanEscape(WebApplication app, SandboxLauncherOptions options)
    {
        _app = app;
        Options = options;
    }

    public SandboxLauncherOptions Options { get; }

    public PodmanApi Api { get; } = new(TraxSandboxLauncher.PodmanClient(Socket ?? ""));

    public static bool Required =>
        string.Equals(
            Environment.GetEnvironmentVariable("TRAX_SANDBOX_ESCAPE_REQUIRED"),
            "1",
            StringComparison.Ordinal
        );

    /// <summary>Skips the test unless a podman socket and an escape image were provided; fails it instead when <see cref="Required"/>.</summary>
    public static void RequireEnvironment()
    {
        if (
            !string.IsNullOrEmpty(Socket)
            && File.Exists(Socket)
            && !string.IsNullOrEmpty(Image)
            && !string.IsNullOrEmpty(Holder)
        )
            return;
        const string Missing =
            "The escape suite needs a podman socket and the escape image: set TRAX_SANDBOX_PODMAN_SOCKET, "
            + "TRAX_SANDBOX_IMAGE and TRAX_SANDBOX_HOLDER_IMAGE (build-escape-image.sh prints the last two).";
        if (Required)
            Assert.Fail(Missing + " TRAX_SANDBOX_ESCAPE_REQUIRED is set, so it may not skip.");
        Assert.Ignore(
            Missing + " trax-test has no podman socket; CI's sandbox-escape job runs it under crun."
        );
    }

    public static SandboxLauncherOptions DefaultOptions()
    {
        var options = new SandboxLauncherOptions
        {
            SigningKey = SandboxHosts.Key,
            ListenUrl = "http://127.0.0.1:7300",
            Backend = SandboxBackendKind.Podman,
        };
        options.Profiles["escape"] = new SandboxProfile { Image = Image };
        options.Podman.SocketPath = Socket ?? "";
        options.Podman.HolderImage = Holder;
        options.Podman.HolderCommand.Add("sleep");
        options.Podman.HolderCommand.Add("infinity");
        options.Caps.MaxPids = 64;
        options.Caps.MaxArchiveFiles = 50;
        return options;
    }

    /// <summary>Starts the launcher, running its startup sweep and canary against the real podman.</summary>
    public static async Task<PodmanEscape> StartAsync(
        SandboxLauncherOptions? options = null,
        string? runtime = null,
        bool? assumeIsolated = null
    )
    {
        options ??= DefaultOptions();
        options.Validate();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        Func<CancellationToken, Task<string>> kernel =
            (assumeIsolated ?? AssumeIsolated)
                ? _ => Task.FromResult("assumed isolated by TRAX_SANDBOX_ASSUME_ISOLATED")
                : RuntimeCanary.ReadHostKernelAsync;
        var app = TraxSandboxLauncher.Build(
            builder,
            options,
            (sp, o) =>
                new PodmanSandboxBackend(
                    new PodmanApi(TraxSandboxLauncher.PodmanClient(o.Podman.SocketPath)),
                    o,
                    runtime ?? Runtime,
                    TimeProvider.System,
                    sp.GetRequiredService<ILogger<PodmanSandboxBackend>>()
                ),
            kernel
        );
        try
        {
            await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return new PodmanEscape(app, options);
    }

    /// <summary>The hub's client of this launcher.</summary>
    public Trax.Scheduler.Services.Sandbox.TraxSandboxClient Client(ServiceProvider hub) =>
        SandboxHosts.Client(
            hub,
            _app.GetTestServer().CreateHandler(),
            new SandboxClientOptions
            {
                LauncherUrl = "http://localhost",
                SigningKey = SandboxHosts.Key,
            }
        );

    /// <summary>The launcher's own HTTP client, for requests a hub's client would never make.</summary>
    public HttpClient Raw() => _app.GetTestClient();

    /// <summary>The containers, pods and volumes the launcher made that still exist.</summary>
    public async Task<int> LeftoversAsync()
    {
        var containers = await Api.ListAsync(
                volumes: false,
                PodmanSandboxBackend.LauncherLabel,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        var volumes = await Api.ListAsync(
                volumes: true,
                PodmanSandboxBackend.LauncherLabel,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        var pods = await Api.ListAsync(
                PodmanKind.Pod,
                PodmanSandboxBackend.LauncherLabel,
                CancellationToken.None
            )
            .ConfigureAwait(false);
        return containers.Count + volumes.Count + pods.Count;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
