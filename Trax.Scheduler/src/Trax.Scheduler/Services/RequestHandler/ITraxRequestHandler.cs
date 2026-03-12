using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Services.RequestHandler;

/// <summary>
/// Hosting-agnostic handler for remote job execution and synchronous run requests.
/// </summary>
/// <remarks>
/// This is the core logic shared by ASP.NET endpoints (<c>UseTraxJobRunner()</c>,
/// <c>UseTraxRunEndpoint()</c>), SQS handlers, and Lambda base classes.
/// It handles input deserialization, type resolution, train execution,
/// and output serialization.
/// </remarks>
public interface ITraxRequestHandler
{
    /// <summary>
    /// Executes a queued job (fire-and-forget path).
    /// Deserializes input via <see cref="Utilities.TypeResolver"/>, runs the job through
    /// <see cref="Trains.JobRunner.IJobRunnerTrain"/>.
    /// </summary>
    /// <param name="request">The remote job request containing metadata ID and optional serialized input</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The result containing the metadata ID of the executed job</returns>
    /// <exception cref="Exception">Thrown when job execution fails — caller decides error representation</exception>
    Task<ExecuteJobResult> ExecuteJobAsync(
        RemoteJobRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Runs a train synchronously and returns the serialized output (direct run path).
    /// Catches train failures and wraps them in <see cref="RemoteRunResponse"/> with
    /// <see cref="RemoteRunResponse.IsError"/> set to <c>true</c>.
    /// </summary>
    /// <param name="request">The remote run request containing train name and serialized input</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The response containing serialized output or error details</returns>
    Task<RemoteRunResponse> RunTrainAsync(RemoteRunRequest request, CancellationToken ct = default);
}

/// <summary>
/// Result of a successful job execution via <see cref="ITraxRequestHandler.ExecuteJobAsync"/>.
/// </summary>
/// <param name="MetadataId">The metadata ID of the executed job</param>
public record ExecuteJobResult(long MetadataId);
