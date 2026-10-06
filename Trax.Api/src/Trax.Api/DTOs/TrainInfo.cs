namespace Trax.Api.DTOs;

/// <summary>
/// A train available in the system, including its input schema for API consumers.
/// </summary>
public record TrainInfo(
    string ServiceTypeName,
    string ImplementationTypeName,
    string InputTypeName,
    string OutputTypeName,
    string Lifetime,
    IReadOnlyList<InputPropertySchema> InputSchema,
    IReadOnlyList<string> RequiredPolicies,
    IReadOnlyList<string> RequiredRoles,
    bool IsQuery,
    bool IsMutation,
    string? GraphQLName,
    bool IsBroadcastEnabled
)
{
    /// <summary>
    /// The train's canonical name: its service interface's FullName, which every other
    /// operations field that takes a train keys on (<c>workQueue.queueTrain</c>,
    /// <c>workQueue.runTrain</c>, <c>trainStats</c>, <c>executions(trainName:)</c>,
    /// <c>workQueues(trainName:)</c>). <see cref="ServiceTypeName"/> is a friendly name for
    /// display and is not accepted by those fields.
    /// </summary>
    public required string FullName { get; init; }

    /// <summary>
    /// Whether the train overrides <c>QueueSubjectKey</c>, so its queued runs for one subject run
    /// one at a time (an override may still return no key for a given input). A run started with
    /// <c>workQueue.runTrain</c> bypasses that serialization and may run alongside queued or
    /// in-flight work for the same subject, which is what the dashboard's Run dialog warns about.
    /// </summary>
    public bool HasQueueSubjectKey { get; init; }
}
