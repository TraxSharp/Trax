using Trax.Scheduler.Trains.TaskServerExecutor;

namespace Trax.Scheduler.Services.BackgroundTaskServer;

/// <summary>
/// In-memory implementation of <see cref="IBackgroundTaskServer"/> for testing purposes.
/// </summary>
/// <remarks>
/// This implementation executes jobs immediately and synchronously (awaitable) without
/// any external infrastructure like Hangfire or Quartz. It's useful for:
/// - Unit and integration testing
/// - Local development without background job infrastructure
/// - Simple scenarios where background processing isn't needed
///
/// Jobs are executed inline when <see cref="EnqueueAsync"/> is called, so the method
/// returns only after the train completes.
///
/// Example usage:
/// ```csharp
/// services.AddTraxEffects(options => options
///     .AddScheduler(scheduler => scheduler.UseInMemoryTaskServer())
/// );
/// ```
/// </remarks>
public class InMemoryTaskServer(ITaskServerExecutorTrain taskServerExecutorTrain)
    : IBackgroundTaskServer
{
    private int _jobCounter;

    /// <inheritdoc />
    /// <remarks>
    /// Executes the train immediately and synchronously. The returned job ID is
    /// a simple incrementing counter prefixed with "inmemory-".
    /// </remarks>
    public Task<string> EnqueueAsync(long metadataId) =>
        EnqueueAsync(metadataId, CancellationToken.None);

    /// <inheritdoc />
    public Task<string> EnqueueAsync(long metadataId, object input) =>
        EnqueueAsync(metadataId, input, CancellationToken.None);

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken)
    {
        var jobId = $"inmemory-{Interlocked.Increment(ref _jobCounter)}";

        await taskServerExecutorTrain.Run(
            new ExecuteManifestRequest(metadataId),
            cancellationToken
        );

        return jobId;
    }

    /// <inheritdoc />
    public async Task<string> EnqueueAsync(
        long metadataId,
        object input,
        CancellationToken cancellationToken
    )
    {
        var jobId = $"inmemory-{Interlocked.Increment(ref _jobCounter)}";

        await taskServerExecutorTrain.Run(
            new ExecuteManifestRequest(metadataId, input),
            cancellationToken
        );

        return jobId;
    }
}
