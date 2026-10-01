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
}
