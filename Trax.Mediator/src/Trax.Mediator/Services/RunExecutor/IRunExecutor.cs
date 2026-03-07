using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Services.RunExecutor;

/// <summary>
/// Abstracts where a "run" train executes — locally (in-process) or remotely (via HTTP).
/// </summary>
/// <remarks>
/// Analogous to <c>IJobSubmitter</c> for queued trains. The default implementation
/// (<see cref="LocalRunExecutor"/>) executes via <c>ITrainBus.RunAsync</c> in the current process.
/// <c>UseRemoteRun()</c> replaces it with an HTTP client that POSTs to a remote endpoint.
/// </remarks>
public interface IRunExecutor
{
    /// <summary>
    /// Executes a train by name and returns its metadata ID and output.
    /// </summary>
    /// <param name="trainName">The fully qualified service type name of the train.</param>
    /// <param name="input">The deserialized train input object.</param>
    /// <param name="outputType">The expected CLR output type of the train.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The metadata ID and output of the completed train.</returns>
    Task<RunTrainResult> ExecuteAsync(
        string trainName,
        object input,
        Type outputType,
        CancellationToken ct = default
    );
}
