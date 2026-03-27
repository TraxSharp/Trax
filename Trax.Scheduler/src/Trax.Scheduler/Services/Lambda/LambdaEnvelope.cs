namespace Trax.Scheduler.Services.Lambda;

/// <summary>
/// Discriminated envelope for direct AWS Lambda invocation payloads.
/// Used by <c>UseLambdaWorkers()</c> on the sender side and <c>TraxLambdaFunction</c>
/// on the receiver side to distinguish between job execution and synchronous run requests
/// without URL path routing.
/// </summary>
/// <param name="Type">The operation to perform</param>
/// <param name="PayloadJson">The JSON-serialized inner request (<see cref="JobSubmitter.RemoteJobRequest"/> or <see cref="RunExecutor.RemoteRunRequest"/>)</param>
public record LambdaEnvelope(LambdaRequestType Type, string PayloadJson);

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
