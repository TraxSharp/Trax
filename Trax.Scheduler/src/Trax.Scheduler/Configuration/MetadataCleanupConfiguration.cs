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
/// Additional train types can be added via <see cref="AddTrainType{TTrain}()"/>
/// or <see cref="AddTrainType(string)"/>, each optionally with a retention period of its own.
/// </remarks>
public class MetadataCleanupConfiguration
{
    /// <summary>
    /// The interval at which the cleanup service runs.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long to retain metadata before it becomes eligible for deletion, for every train that
    /// was not given a retention of its own.
    /// </summary>
    /// <remarks>
    /// Only metadata in a terminal state (Completed or Failed) older than this
    /// period will be deleted. Pending or InProgress metadata is never cleaned up.
    ///
    /// <para>This is the value the persisted runtime override replaces. A retention passed to
    /// <see cref="AddTrainType{TTrain}(TimeSpan)"/> is set in code and is not affected by it: a
    /// per-train value is a deliberate exception, often driven by what the input column holds, and
    /// an edit to "the retention" should not silently shorten it.</para>
    /// </remarks>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum number of metadata rows deleted per batch. Limits row-level lock duration
    /// during cleanup. Set to null for single-statement deletes (pre-batch behavior).
    /// </summary>
    /// <remarks>
    /// The limit is per batch, and trains sharing a retention are swept as one group, so a
    /// cleanup cycle deletes up to this many rows per group rather than in total.
    /// </remarks>
    public int? DeleteBatchSize { get; set; } = 1000;

    private readonly List<string> _trainTypes = [];
    private readonly Dictionary<string, TimeSpan?> _retentions = new(StringComparer.Ordinal);

    /// <summary>
    /// The train type names whose metadata should be cleaned up, in the order they were added.
    /// </summary>
    /// <remarks>
    /// Names are matched against the <c>name</c> column in the metadata table,
    /// which stores the interface FullName (the canonical train name).
    /// </remarks>
    internal IReadOnlyList<string> TrainTypeWhitelist => _trainTypes;

    /// <summary>
    /// Each whitelisted name against the retention it was added with, or <c>null</c> for
    /// <see cref="RetentionPeriod"/>.
    /// </summary>
    internal IReadOnlyDictionary<string, TimeSpan?> TrainTypeRetentions => _retentions;

    /// <summary>
    /// Adds a train type to the cleanup whitelist by its class name, retained for
    /// <see cref="RetentionPeriod"/>.
    /// </summary>
    /// <typeparam name="TTrain">The train class type to clean up</typeparam>
    public void AddTrainType<TTrain>()
        where TTrain : class => Add(typeof(TTrain).FullName!, null);

    /// <summary>
    /// Adds a train type to the cleanup whitelist by its class name, retained for
    /// <paramref name="retention"/> instead of <see cref="RetentionPeriod"/>.
    /// </summary>
    /// <typeparam name="TTrain">The train class type to clean up</typeparam>
    /// <param name="retention">
    /// How long this train's metadata is kept. Must be positive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retention"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// The train is an internal scheduler train, or it was already added with a different
    /// retention.
    /// </exception>
    public void AddTrainType<TTrain>(TimeSpan retention)
        where TTrain : class => Add(typeof(TTrain).FullName!, retention);

    /// <summary>
    /// Adds a train type to the cleanup whitelist by name string, retained for
    /// <see cref="RetentionPeriod"/>.
    /// </summary>
    /// <param name="trainTypeName">
    /// The train type name as it appears in the metadata <c>name</c> column.
    /// </param>
    public void AddTrainType(string trainTypeName) => Add(trainTypeName, null);

    /// <summary>
    /// Adds a train type to the cleanup whitelist by name string, retained for
    /// <paramref name="retention"/> instead of <see cref="RetentionPeriod"/>.
    /// </summary>
    /// <param name="trainTypeName">
    /// The train type name as it appears in the metadata <c>name</c> column.
    /// </param>
    /// <param name="retention">How long this train's metadata is kept. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retention"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// The train is an internal scheduler train, or it was already added with a different
    /// retention.
    /// </exception>
    public void AddTrainType(string trainTypeName, TimeSpan retention) =>
        Add(trainTypeName, retention);

    private void Add(string trainTypeName, TimeSpan? retention)
    {
        if (retention is { } value && value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(retention),
                retention,
                $"Retention for '{trainTypeName}' must be positive. A zero or negative retention "
                    + "would make every terminal row of that train immediately eligible for deletion."
            );

        // The internal scheduler trains are swept unconditionally so a consumer cannot forget one,
        // and the dispatcher alone writes a row every poll. Lengthening one is exactly the
        // unbounded growth that rule exists to prevent, so it is refused rather than ignored:
        // silently dropping a 30-day retention is discovered a month later, if at all.
        if (retention is not null && AdminTrains.FullNames.Contains(trainTypeName))
            throw new InvalidOperationException(
                $"'{trainTypeName}' is an internal scheduler train and is always cleaned up at "
                    + $"{nameof(RetentionPeriod)}. It cannot be given a retention of its own."
            );

        if (_retentions.TryGetValue(trainTypeName, out var existing))
        {
            if (existing != retention)
                throw new InvalidOperationException(
                    $"'{trainTypeName}' was already added for metadata cleanup with retention "
                        + $"{Describe(existing)}, and cannot be added again with {Describe(retention)}. "
                        + "Declare it once."
                );

            return;
        }

        _retentions[trainTypeName] = retention;
        _trainTypes.Add(trainTypeName);
    }

    private static string Describe(TimeSpan? retention) =>
        retention?.ToString() ?? $"the default ({nameof(RetentionPeriod)})";
}
