using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Trax.Scheduler.Sandbox.Launcher;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>The real launcher host over a fake backend, in memory.</summary>
internal sealed class LauncherServer : IAsyncDisposable
{
    public const string HostKernel = "Linux version host";

    private readonly WebApplication _app;

    private LauncherServer(WebApplication app, FakeBackend backend)
    {
        _app = app;
        Backend = backend;
        Client = app.GetTestClient();
    }

    public FakeBackend Backend { get; }

    public HttpClient Client { get; }

    public static async Task<LauncherServer> StartAsync(
        FakeBackend? backend = null,
        SandboxLauncherOptions? options = null
    )
    {
        backend ??= new FakeBackend();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        var app = TraxSandboxLauncher.Build(
            builder,
            options ?? LauncherOptions.Podman(),
            (_, _) => backend,
            _ => Task.FromResult(HostKernel)
        );
        await app.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return new LauncherServer(app, backend);
    }

    /// <summary>Posts <paramref name="body"/> signed now (or with <paramref name="signature"/>), and returns the answer, its body and the signature it was sent with.</summary>
    public async Task<(HttpResponseMessage Response, byte[] Body, string Signature)> PostAsync(
        string body,
        string? signature = null,
        byte[]? key = null
    )
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        signature ??= SandboxChannelSignature.Sign(
            key ?? SandboxHosts.Key,
            bytes,
            TimeProvider.System
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, "/trax/sandbox/run")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Headers.TryAddWithoutValidation(SandboxChannelSignature.HeaderName, signature);
        var response = await Client
            .SendAsync(request, CancellationToken.None)
            .ConfigureAwait(false);
        return (
            response,
            await response
                .Content.ReadAsByteArrayAsync(CancellationToken.None)
                .ConfigureAwait(false),
            signature
        );
    }

    public static string Request(
        string runId = "0123456789abcdef0123456789abcdef",
        int timeout = 30
    ) =>
        JsonSerializer.Serialize(
            new SandboxRunRequest(
                runId,
                "escape",
                "Trax.Scheduler.Sandbox.Image.IHostileTrain",
                "{}",
                new SandboxLimits(500, 192, 8, timeout)
            ),
            SandboxJson.Options
        );

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }
}
