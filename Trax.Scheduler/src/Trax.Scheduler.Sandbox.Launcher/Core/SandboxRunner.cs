using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// Runs one checked request: takes a slot, hands the backend a job the launcher built, and turns
/// what the backend saw into the answer. The result file and the files the sandbox left are
/// checked here as well as by the backend, and only files under the output's <c>files/</c>
/// directory go back to the hub.
/// </summary>
internal sealed class SandboxRunner(
    ISandboxBackend backend,
    SandboxLauncherOptions options,
    SandboxGate gate,
    ILogger<SandboxRunner> logger
)
{
    private const string FilesPrefix = SandboxPaths.Files + "/";

    public async Task<SandboxRunResponse> RunAsync(
        SandboxRunRequest request,
        SandboxProfile profile,
        CancellationToken ct
    )
    {
        using var slot = gate.TryEnter();
        if (slot is null)
            return new SandboxRunResponse(
                request.RunId,
                SandboxOutcome.Busy,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"this launcher is already running its cap of {options.Caps.MaxConcurrent} sandboxes"
                )
            );

        var job = new SandboxJob(
            request.RunId,
            profile,
            request.Limits,
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [SandboxPaths.RequestFile] = JsonSerializer.SerializeToUtf8Bytes(
                    request,
                    SandboxJson.Options
                ),
            }
        );

        logger.LogInformation(
            "Starting sandbox {RunId} for {TrainName} in profile {Profile}.",
            request.RunId,
            request.TrainName,
            request.Profile
        );
        var execution = await backend.RunAsync(job, ct).ConfigureAwait(false);
        var response = await AnswerAsync(request, execution, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Sandbox {RunId} ended {Outcome}: {Reason}",
            request.RunId,
            response.Outcome,
            response.Reason ?? "completed"
        );
        return response;
    }

    /// <summary>The answer for a sandbox that did not exit by itself; null when it did. A killed run hands nothing back.</summary>
    private static SandboxRunResponse? Killed(SandboxRunRequest request, SandboxEnd end)
    {
        var limits = request.Limits;
        switch (end)
        {
            case SandboxEnd.Failed:
                return new(
                    request.RunId,
                    SandboxOutcome.Failed,
                    "the launcher could not run the sandbox; its log has the detail"
                );
            case SandboxEnd.TimedOut:
                return new(
                    request.RunId,
                    SandboxOutcome.TimedOut,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"it ran past its {limits.TimeoutSeconds} second timeout and was killed"
                    )
                );
            case SandboxEnd.OutOfMemory:
                return new(
                    request.RunId,
                    SandboxOutcome.OutOfMemory,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"it went over its {limits.MemoryMiB} MiB of memory and was killed"
                    )
                );
            case SandboxEnd.DiskExceeded:
                return new(
                    request.RunId,
                    SandboxOutcome.Exited,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"it wrote more than its {limits.DiskMiB} MiB of output and was killed"
                    )
                );
        }

        return null;
    }

    private async Task<SandboxRunResponse> AnswerAsync(
        SandboxRunRequest request,
        SandboxExecution execution,
        CancellationToken ct
    )
    {
        if (Killed(request, execution.End) is { } killed)
            return killed;

        if (execution.OutputRefused is { } refused)
            return new(request.RunId, SandboxOutcome.OutputRefused, refused);

        var result = execution.Output.FirstOrDefault(f =>
            string.Equals(f.Path, SandboxPaths.ResultFile, StringComparison.Ordinal)
        );
        if (execution.ExitCode != 0 || result is null)
            return new(
                request.RunId,
                SandboxOutcome.Exited,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"it exited with code {execution.ExitCode}"
                ) + (result is null ? " and left no result" : "")
            );

        if (result.Content.Length > options.Caps.MaxResultBytes || !IsJson(result.Content))
            return new(
                request.RunId,
                SandboxOutcome.Exited,
                "it left a result that is too large or not JSON"
            );

        var files = execution
            .Output.Where(f => f.Path.StartsWith(FilesPrefix, StringComparison.Ordinal))
            .Select(f => f with { Path = f.Path[FilesPrefix.Length..] })
            .ToList();
        if (files.Sum(f => (long)f.Content.Length) > options.Caps.MaxArchiveBytes)
            return new(
                request.RunId,
                SandboxOutcome.OutputRefused,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"its files are larger than {options.Caps.MaxArchiveBytes} bytes"
                )
            );

        var archive =
            files.Count == 0
                ? null
                : Convert.ToBase64String(
                    await SandboxArchive.WriteAsync(files, ct).ConfigureAwait(false)
                );
        return new(
            request.RunId,
            SandboxOutcome.Completed,
            Reason: null,
            Encoding.UTF8.GetString(result.Content.Span),
            archive
        );
    }

    private static bool IsJson(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var _ = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
