using Trax.Scheduler.Workflows.TaskServerExecutor;

namespace Trax.Scheduler.Services.BackgroundTaskServer;

/// <summary>
/// Abstraction over background task servers (Hangfire, Quartz, etc.) for enqueuing and scheduling jobs.
/// </summary>
/// <remarks>
/// This interface provides a provider-agnostic way to enqueue background tasks.
/// Implementations can wrap Hangfire, Quartz.NET, or any other background job processing library.
///
/// The abstraction allows Trax.Scheduler to remain decoupled from specific
/// background task implementations, enabling consumers to use their preferred infrastructure.
///
/// Example implementations:
/// - HangfireTaskServer (in Trax.Scheduler.Hangfire)
/// - QuartzTaskServer (in Trax.Scheduler.Quartz)
///
/// Implementations should resolve <see cref="ITaskServerExecutorWorkflow"/> from the DI container
/// and call <see cref="ITaskServerExecutorWorkflow.Run"/> with an <see cref="ExecuteManifestRequest"/>.
/// </remarks>
public interface IBackgroundTaskServer
{
    /// <summary>
    /// Enqueues a job for immediate execution.
    /// </summary>
    /// <param name="metadataId">The ID of the Metadata record representing this job execution</param>
    /// <returns>A background task identifier for correlation/tracking (provider-specific)</returns>
    /// <remarks>
    /// The job will be picked up by a worker as soon as one is available.
    /// The returned identifier is provider-specific (e.g., Hangfire job ID) and can be
    /// stored for later correlation with the background task server's native tracking system.
    ///
    /// Implementations should enqueue a call to <see cref="ITaskServerExecutorWorkflow.Run"/>
    /// with an <see cref="ExecuteManifestRequest"/> containing the provided metadata ID.
    /// </remarks>
    Task<string> EnqueueAsync(long metadataId);

    /// <summary>
    /// Enqueues a job for immediate execution with an in-memory workflow input.
    /// </summary>
    /// <param name="metadataId">The ID of the Metadata record representing this job execution</param>
    /// <param name="input">The workflow input object to pass to the executor</param>
    /// <returns>A background task identifier for correlation/tracking (provider-specific)</returns>
    /// <remarks>
    /// Used for ad-hoc workflow executions (e.g., from the dashboard) where the input
    /// is provided directly rather than resolved from a Manifest's properties.
    /// </remarks>
    Task<string> EnqueueAsync(long metadataId, object input);

    /// <summary>
    /// Enqueues a job for immediate execution with cancellation support.
    /// </summary>
    Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken) =>
        EnqueueAsync(metadataId);

    /// <summary>
    /// Enqueues a job for immediate execution with an in-memory workflow input and cancellation support.
    /// </summary>
    Task<string> EnqueueAsync(long metadataId, object input, CancellationToken cancellationToken) =>
        EnqueueAsync(metadataId, input);
}
