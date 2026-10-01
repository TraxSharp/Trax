using System.ComponentModel;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Services.RequestHandler;

/// <summary>
/// The runner's execution step, behind its entry points: it reads a request's input, resolves
/// the registered train, and runs it. It checks no posture and verifies no signature.
/// </summary>
/// <remarks>
/// <para>
/// The entry points Trax ships (<c>UseTraxJobRunner()</c>, <c>UseTraxRunEndpoint()</c>,
/// <c>SqsJobRunnerHandler</c>, <c>TraxLambdaFunction</c>) verify each request against the
/// runner's posture before they call it, and it runs what it is given inside a trusted execution
/// scope, so a train's <c>[TraxAuthorize]</c> requirements do not apply (scheduler/0006).
/// </para>
/// <para>
/// Call it only from an entry point that has already verified the request: its
/// <c>Trax-Signature</c> with <c>RunnerRequestVerifier.VerifyAsync</c>, or an authorization
/// policy that admits only the scheduler. Code that hands it a request it has not verified lets
/// whoever sent that request run any registered train. Hidden from IntelliSense for that reason;
/// a host serves a runner through the entry points above.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ITraxRequestHandler
{
    /// <summary>
    /// Executes a queued job (fire-and-forget path).
    /// Deserializes the input into the registered train input type whose full name the request
    /// gives (a name no registered train takes is refused), then runs the job through
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
