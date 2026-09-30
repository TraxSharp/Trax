namespace Trax.Effect.Data.Postgres.Utils;

/// <summary>
/// How long a migration script waits for a table lock, and how often the migrator runs the
/// pending scripts again after one of them gave up on its lock.
/// </summary>
/// <param name="LockTimeout">The script session's <c>lock_timeout</c>.</param>
/// <param name="Attempts">How many times the pending scripts are run before the migration fails.</param>
/// <param name="Backoff">How long the migrator waits after a timed-out attempt, times the attempt number.</param>
internal sealed record MigrationLockPolicy(TimeSpan LockTimeout, int Attempts, TimeSpan Backoff)
{
    /// <summary>
    /// Five seconds per wait, ten tries: a start behind a stuck transaction fails in about a
    /// minute, and no enqueue on another instance waits behind a script for more than five seconds.
    /// </summary>
    public static MigrationLockPolicy Default { get; } =
        new(TimeSpan.FromSeconds(5), Attempts: 10, Backoff: TimeSpan.FromSeconds(1));
}
