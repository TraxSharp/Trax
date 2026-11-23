using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Services.JobSubmitter;

/// <summary>
/// Abstraction over job submission backends for enqueuing scheduled train executions.
/// </summary>
/// <remarks>
/// This interface provides a provider-agnostic way to submit jobs for execution.
/// The built-in implementations are:
/// - <see cref="PostgresJobSubmitter"/>, which inserts into the <c>background_job</c> table for the local
///   workers, on any database provider
/// - <c>InMemoryJobSubmitter</c>, internal, which runs the job inline on a host with no database provider
///
/// Custom implementations can submit jobs to external systems (AWS Lambda, Azure Functions, etc.).
///
/// Implementations should resolve <see cref="IJobRunnerTrain"/> from the DI container
/// and call <c>IJobRunnerTrain.Run</c> with a <see cref="RunJobRequest"/>.
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
    /// Implementations should enqueue a call to <c>IJobRunnerTrain.Run</c>
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
        EnqueueWithoutToken(this, metadataId);

    /// <summary>
    /// Enqueues a job for immediate execution with an in-memory train input and cancellation support.
    /// </summary>
    Task<string> EnqueueAsync(long metadataId, object input, CancellationToken cancellationToken) =>
        EnqueueWithoutToken(this, metadataId, input);

    /// <summary>
    /// Enqueues a job with a dispatch priority. Higher values are dequeued first by workers.
    /// </summary>
    /// <remarks>
    /// The default implementation falls back to the non-priority overload, so existing
    /// custom implementations continue to work without modification (priority is ignored).
    /// Only <see cref="PostgresJobSubmitter"/> stores the priority in the background_job table.
    /// </remarks>
    Task<string> EnqueueAsync(long metadataId, int priority, CancellationToken cancellationToken) =>
        EnqueueAsync(metadataId, cancellationToken);

    /// <summary>
    /// Enqueues a job with input and a dispatch priority. Higher values are dequeued first by workers.
    /// </summary>
    Task<string> EnqueueAsync(
        long metadataId,
        object input,
        int priority,
        CancellationToken cancellationToken
    ) => EnqueueAsync(metadataId, input, cancellationToken);

    // The token overloads fall back to the ones without a token, so an implementation written
    // before they existed keeps working. The call is made from here, where no token is in scope,
    // so it cannot be mistaken for one that should forward it.
    private static Task<string> EnqueueWithoutToken(IJobSubmitter submitter, long metadataId) =>
        submitter.EnqueueAsync(metadataId);

    private static Task<string> EnqueueWithoutToken(
        IJobSubmitter submitter,
        long metadataId,
        object input
    ) => submitter.EnqueueAsync(metadataId, input);
}
