namespace Trax.Scheduler.Services.Lambda;

/// <summary>
/// Identifies the operation type within a <see cref="LambdaEnvelope"/>.
/// </summary>
public enum LambdaRequestType
{
    /// <summary>
    /// Fire-and-forget job execution. The inner payload is a <see cref="JobSubmitter.RemoteJobRequest"/>.
    /// </summary>
    Execute,

    /// <summary>
    /// Synchronous train run. The inner payload is a <see cref="RunExecutor.RemoteRunRequest"/>.
    /// </summary>
    Run,
}
