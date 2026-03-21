using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Trax.Samples.GameServer.Hooks;

/// <summary>
/// Lifecycle hook that logs train completion and failure events to the console.
/// Demonstrates how to create a custom hook with no boilerplate — just implement
/// <see cref="ITrainLifecycleHook"/> and register with <c>AddLifecycleHook&lt;AuditLogHook&gt;()</c>.
/// </summary>
public class AuditLogHook(ILogger<AuditLogHook> logger) : ITrainLifecycleHook
{
    public Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        var duration = metadata.EndTime - metadata.StartTime;

        logger.LogInformation(
            "[AUDIT] {Train} completed in {Duration}ms (ext: {ExternalId})",
            metadata.Name.Split('.').Last(),
            duration?.TotalMilliseconds,
            metadata.ExternalId
        );

        return Task.CompletedTask;
    }

    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        logger.LogWarning(
            "[AUDIT] {Train} failed at junction {Junction}: {Error}",
            metadata.Name.Split('.').Last(),
            metadata.FailureJunction,
            exception.Message
        );

        return Task.CompletedTask;
    }
}
