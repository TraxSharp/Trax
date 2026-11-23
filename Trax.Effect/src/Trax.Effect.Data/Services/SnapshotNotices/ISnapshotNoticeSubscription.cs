namespace Trax.Effect.Data.Services.SnapshotNotices;

/// <summary>One listening session opened by <see cref="ISnapshotNoticeSource.SubscribeAsync"/>.</summary>
internal interface ISnapshotNoticeSubscription : IAsyncDisposable
{
    /// <summary>
    /// Completes with the payload of the next notice, exactly as it was sent (a forged one included), or with null
    /// when <paramref name="idle"/> passes without one. Throws when the session is lost, for example when its
    /// database connection is closed; dispose it and subscribe again.
    /// </summary>
    Task<string?> NextAsync(TimeSpan idle, CancellationToken cancellationToken);

    /// <summary>
    /// How full the database's notification queue is, from 0 to 1 (<c>pg_notification_queue_usage()</c>), read on
    /// this session. Call it only between <see cref="NextAsync"/> calls.
    /// </summary>
    Task<double> QueueUsageAsync(CancellationToken cancellationToken);
}
