namespace Trax.Effect.Attributes;

/// <summary>
/// Marks a train for remote execution when a remote submitter is configured.
/// Trains decorated with this attribute will be dispatched to the configured remote
/// worker (HTTP or SQS) instead of being executed by local worker threads.
/// </summary>
/// <remarks>
/// Place this attribute on the concrete train class. If no remote submitter is configured
/// (e.g., <c>UseRemoteWorkers()</c> or <c>UseSqsWorkers()</c>), the attribute is silently
/// ignored and the train runs locally.
///
/// Builder-level routing via <c>ForTrain&lt;T&gt;()</c> takes precedence over this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxRemoteAttribute : Attribute { }
