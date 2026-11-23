namespace Trax.Effect.Data.Services.SnapshotNotices;

/// <summary>
/// What decides whether a write to <c>trax.snapshot_draft</c> sends a notice, as one host reads it.
/// </summary>
/// <param name="TriggerState">
/// The trigger's <c>pg_trigger.tgenabled</c>: <c>O</c> fires for ordinary sessions, <c>A</c> always, <c>R</c> for
/// replication sessions only, <c>D</c> never; null when there is no trigger.
/// </param>
/// <param name="TriggerHasCondition">Whether the trigger reads the switch (080); one without a condition always fires.</param>
/// <param name="DatabaseValue">The database's own setting (<c>ALTER DATABASE ... SET</c>), or null.</param>
/// <param name="SessionValue">The setting a new session of this host reads, or null.</param>
/// <param name="SessionIsReplica">Whether this host's session starts with <c>session_replication_role = replica</c>.</param>
/// <param name="RolesTurningOff">The roles whose own setting turns the notices off in this database.</param>
internal sealed record SnapshotNoticeSwitch(
    string? TriggerState,
    bool TriggerHasCondition,
    string? DatabaseValue,
    string? SessionValue,
    bool SessionIsReplica,
    IReadOnlyList<string> RolesTurningOff
)
{
    /// <summary>The setting's name.</summary>
    public const string Setting = "trax.snapshot_notify";

    /// <summary>Whether <paramref name="value"/> turns the notices off: <c>off</c> in any case, as the trigger reads it.</summary>
    public static bool IsOff(string? value) =>
        string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="value"/> is unset, <c>on</c> or <c>off</c>, in any case.</summary>
    public static bool IsKnown(string? value) =>
        value is null
        || IsOff(value)
        || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the trigger fires for an ordinary session.</summary>
    public bool TriggerFires => TriggerState is "O" or "A";

    /// <summary>Whether a write from a session that takes the database's setting sends a notice.</summary>
    public bool DatabaseSends => TriggerFires && (!TriggerHasCondition || !IsOff(DatabaseValue));

    /// <summary>Whether a write from a new session of this host sends a notice.</summary>
    public bool SessionSends =>
        (TriggerState is "A" || (TriggerState is "O" && !SessionIsReplica))
        && (!TriggerHasCondition || !IsOff(SessionValue));
}
