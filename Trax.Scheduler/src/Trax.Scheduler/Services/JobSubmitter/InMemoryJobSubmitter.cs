using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// In-memory implementation of <see cref="IJobSubmitter"/> for testing purposes.
/// </summary>
/// <remarks>
/// This implementation executes jobs immediately and synchronously (awaitable) without
/// any external infrastructure. It's useful for:
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
///     .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
/// );
/// ```
/// </remarks>
public class InMemoryJobSubmitter(IJobRunnerTrain jobRunnerTrain) : IJobSubmitter
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

        await jobRunnerTrain.Run(new RunJobRequest(metadataId), cancellationToken);

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

        await jobRunnerTrain.Run(new RunJobRequest(metadataId, input), cancellationToken);

        return jobId;
    }
}
