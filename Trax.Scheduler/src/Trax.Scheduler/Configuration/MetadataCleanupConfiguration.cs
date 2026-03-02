namespace Trax.Scheduler.Configuration;

/// <summary>
/// Configuration options for the metadata cleanup background service.
/// </summary>
/// <remarks>
/// Controls which train types have their metadata automatically purged
/// and how aggressively old entries are cleaned up.
///
/// Default behavior cleans up <c>ManifestManagerTrain</c> and
/// <c>MetadataCleanupTrain</c> metadata older than 1 hour, running every minute.
///
/// Additional train types can be added via <see cref="AddTrainType{TTrain}"/>
/// or <see cref="AddTrainType(string)"/>.
/// </remarks>
public class MetadataCleanupConfiguration
{
    /// <summary>
    /// The interval at which the cleanup service runs.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long to retain metadata before it becomes eligible for deletion.
    /// </summary>
    /// <remarks>
    /// Only metadata in a terminal state (Completed or Failed) older than this
    /// period will be deleted. Pending or InProgress metadata is never cleaned up.
    /// </remarks>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The list of train type names whose metadata should be cleaned up.
    /// </summary>
    /// <remarks>
    /// Names are matched against the <c>name</c> column in the metadata table,
    /// which stores <c>GetType().FullName</c> of the train class.
    /// </remarks>
    internal List<string> TrainTypeWhitelist { get; } = [];

    /// <summary>
    /// Adds a train type to the cleanup whitelist by its class name.
    /// </summary>
    /// <typeparam name="TTrain">The train class type to clean up</typeparam>
    public void AddTrainType<TTrain>()
        where TTrain : class
    {
        TrainTypeWhitelist.Add(typeof(TTrain).FullName!);
    }

    /// <summary>
    /// Adds a train type to the cleanup whitelist by name string.
    /// </summary>
    /// <param name="trainTypeName">
    /// The train type name as it appears in the metadata <c>name</c> column.
    /// </param>
    public void AddTrainType(string trainTypeName)
    {
        TrainTypeWhitelist.Add(trainTypeName);
    }
}
