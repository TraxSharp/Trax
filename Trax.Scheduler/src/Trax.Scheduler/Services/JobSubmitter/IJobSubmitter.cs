using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// Abstraction over job submission backends for enqueuing scheduled train executions.
/// </summary>
/// <remarks>
/// This interface provides a provider-agnostic way to submit jobs for execution.
/// The built-in implementations are:
/// - <see cref="PostgresJobSubmitter"/> — inserts into the <c>background_job</c> table for local workers
/// - <see cref="InMemoryJobSubmitter"/> — executes inline for testing
///
/// Custom implementations can submit jobs to external systems (AWS Lambda, Azure Functions, etc.).
///
/// Implementations should resolve <see cref="IJobRunnerTrain"/> from the DI container
/// and call <see cref="IJobRunnerTrain.Run"/> with a <see cref="RunJobRequest"/>.
/// </remarks>
public interface IJobSubmitter
{
    /// <summary>
    /// Enqueues a job for immediate execution.
    /// </summary>
    /// <param name="metadataId">The ID of the Metadata record representing this job execution</param>
    /// <returns>A job identifier for correlation/tracking (provider-specific)</returns>
    /// <remarks>
    /// The job will be picked up by a worker as soon as one is available.
    /// The returned identifier is provider-specific and can be stored for later correlation.
    ///
    /// Implementations should enqueue a call to <see cref="IJobRunnerTrain.Run"/>
    /// with a <see cref="RunJobRequest"/> containing the provided metadata ID.
    /// </remarks>
    Task<string> EnqueueAsync(long metadataId);

    /// <summary>
    /// Enqueues a job for immediate execution with an in-memory train input.
    /// </summary>
    /// <param name="metadataId">The ID of the Metadata record representing this job execution</param>
    /// <param name="input">The train input object to pass to the job runner</param>
    /// <returns>A job identifier for correlation/tracking (provider-specific)</returns>
    /// <remarks>
    /// Used for ad-hoc train executions (e.g., from the dashboard) where the input
    /// is provided directly rather than resolved from a Manifest's properties.
    /// </remarks>
    Task<string> EnqueueAsync(long metadataId, object input);

    /// <summary>
    /// Enqueues a job for immediate execution with cancellation support.
    /// </summary>
    Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken) =>
        EnqueueAsync(metadataId);

    /// <summary>
    /// Enqueues a job for immediate execution with an in-memory train input and cancellation support.
    /// </summary>
    Task<string> EnqueueAsync(long metadataId, object input, CancellationToken cancellationToken) =>
        EnqueueAsync(metadataId, input);
}
