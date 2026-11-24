using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>
/// What runs inside a sandbox: reads the request the launcher left in the input directory, runs
/// the train it names when that train carries <see cref="TraxSandboxAttribute"/>, and writes the
/// result to the output volume. Or, for the launcher's startup canary, reports the kernel and
/// network it finds.
/// </summary>
/// <remarks>
/// Everything here runs next to the train, so nothing here is trusted by anyone: the launcher and
/// the hub check what it writes. It holds no credential, and the request it reads came from the
/// hub through the launcher.
/// </remarks>
internal sealed class TraxSandboxEntryPoint(
    IServiceProvider services,
    SandboxWorkspace workspace,
    ILogger<TraxSandboxEntryPoint> logger
)
{
    /// <summary>The largest request file read; the launcher caps the input well below it.</summary>
    internal const long MaxRequestBytes = 64L * 1024 * 1024;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Exit code for a run whose result was written, the train's failure included.</summary>
    internal const int ExitWritten = 0;

    /// <summary>Exit code for a request that was missing or unreadable; a result saying so was written when it could be.</summary>
    internal const int ExitBadRequest = 2;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var canary = Path.Combine(workspace.InputDirectory, SandboxPaths.CanaryFile);
        if (File.Exists(canary))
            return await RunCanaryAsync(canary, ct).ConfigureAwait(false);

        Directory.CreateDirectory(workspace.FilesDirectory);

        SandboxRunRequest request;
        try
        {
            request = await ReadAsync<SandboxRunRequest>(
                    Path.Combine(workspace.InputDirectory, SandboxPaths.RequestFile),
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            logger.LogError(ex, "The sandbox request could not be read.");
            await WriteResultAsync(
                    TraxRequestHandler.BuildErrorResponse(
                        new TrainException("The sandbox request could not be read.")
                    ),
                    ct
                )
                .ConfigureAwait(false);
            return ExitBadRequest;
        }

        var response = await RunTrainAsync(request, ct).ConfigureAwait(false);
        await WriteResultAsync(response, ct).ConfigureAwait(false);
        return ExitWritten;
    }

    private async Task<RemoteRunResponse> RunTrainAsync(
        SandboxRunRequest request,
        CancellationToken ct
    )
    {
        try
        {
            RefuseUnlessSandboxed(request.TrainName);

            RunTrainResult result;
            var scope = services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
                result = await TraxRequestHandler
                    .RunAuthorizedAsync(
                        scope.ServiceProvider.GetRequiredService<ITrainExecutionService>(),
                        scope.ServiceProvider.GetRequiredService<ITrustedExecutionScope>(),
                        "scheduler.sandbox-run",
                        request.TrainName,
                        request.InputJson,
                        ct
                    )
                    .ConfigureAwait(false);

            if (result.Output is null)
                return new RemoteRunResponse(result.MetadataId, result.ExternalId);

            return new RemoteRunResponse(
                result.MetadataId,
                result.ExternalId,
                JsonSerializer.Serialize(
                    result.Output,
                    result.Output.GetType(),
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                result.Output.GetType().FullName
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sandboxed train {TrainName} failed", request.TrainName);
            return TraxRequestHandler.BuildErrorResponse(ex);
        }
    }

    /// <summary>
    /// Refuses a name that is not, exactly, the interface of a registered train whose class
    /// carries <see cref="TraxSandboxAttribute"/>. No short names: a sandbox runs only what was
    /// declared for it.
    /// </summary>
    private void RefuseUnlessSandboxed(string trainName)
    {
        var discovery = services.GetRequiredService<ITrainDiscoveryService>();
        var registration = discovery
            .DiscoverTrains()
            .FirstOrDefault(r =>
                string.Equals(r.ServiceType.FullName, trainName, StringComparison.Ordinal)
            );

        if (registration?.ImplementationType.GetCustomAttribute<TraxSandboxAttribute>() is null)
            throw new TrainException(
                $"'{trainName}' is not a registered train marked [TraxSandbox], so this sandbox "
                    + "does not run it."
            );
    }

    private async Task<int> RunCanaryAsync(string path, CancellationToken ct)
    {
        var request = await ReadAsync<SandboxCanaryRequest>(path, ct).ConfigureAwait(false);

        var kernel = await File.ReadAllTextAsync("/proc/version", ct).ConfigureAwait(false);
        var interfaces = NetworkInterface
            .GetAllNetworkInterfaces()
            .Select(n => n.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var reached = new List<string>();
        foreach (var probe in request.Probes)
            if (await ConnectsAsync(probe, ct).ConfigureAwait(false))
                reached.Add(probe);

        var report = new SandboxCanaryReport(kernel.Trim(), interfaces, reached);
        await File.WriteAllBytesAsync(
                Path.Combine(workspace.OutputDirectory, SandboxPaths.CanaryReportFile),
                JsonSerializer.SerializeToUtf8Bytes(report, SandboxJson.Options),
                ct
            )
            .ConfigureAwait(false);
        return ExitWritten;
    }

    /// <summary>Whether a TCP connection to <paramref name="probe"/> (<c>host:port</c>) opens within the probe timeout.</summary>
    internal static async Task<bool> ConnectsAsync(string probe, CancellationToken ct)
    {
        var colon = probe.LastIndexOf(':');
        if (
            colon <= 0
            || !int.TryParse(
                probe.AsSpan(colon + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var port
            )
        )
            return false;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ProbeTimeout);
        using var client = new TcpClient();
        try
        {
            await client
                .ConnectAsync(probe[..colon].Trim('[', ']'), port, deadline.Token)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static async Task<T> ReadAsync<T>(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new IOException($"'{path}' is missing.");
        if (info.Length > MaxRequestBytes)
            throw new InvalidDataException($"'{path}' is larger than {MaxRequestBytes} bytes.");

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
            return await JsonSerializer
                    .DeserializeAsync<T>(stream, SandboxJson.Options, ct)
                    .ConfigureAwait(false)
                ?? throw new InvalidDataException($"'{path}' holds null.");
    }

    private Task WriteResultAsync(RemoteRunResponse response, CancellationToken ct) =>
        File.WriteAllBytesAsync(
            Path.Combine(workspace.OutputDirectory, SandboxPaths.ResultFile),
            JsonSerializer.SerializeToUtf8Bytes(response, RemoteRunJson.Write),
            ct
        );
}
