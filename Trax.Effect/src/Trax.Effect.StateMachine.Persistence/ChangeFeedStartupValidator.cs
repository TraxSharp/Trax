using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.SnapshotNotices;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Compares the host's <see cref="StateMachineOptions.ChangeFeed"/> with what its database sends, on a provider that
/// sends change notices (Postgres). The trigger reads <c>trax.snapshot_notify</c> in the session that writes, which a
/// listening host cannot see, so the only switch it trusts is the database's own (<c>ALTER DATABASE ... SET</c>), read
/// from the catalog rather than from its session.
/// </summary>
/// <remarks>
/// <para>A host on <see cref="StateMachineChangeFeed.Notify"/> refuses to start when some writer would send no notice
/// that it can tell of: the trigger is missing, disabled or enabled for replication sessions only; the database turns
/// the notices off; a role turns them off for its own sessions; or this host's own sessions send none. A writer that
/// turns them off with its connection string or <c>SET LOCAL</c> is not visible from here, and is not supported while
/// any host listens.</para>
/// <para>A host on <see cref="StateMachineChangeFeed.Poll"/> starts, and warns while the database still notifies:
/// it is correct, and a fleet moving to polling deploys its hosts before it turns the notices off, but every write
/// still pays for a notice nobody needs. Either kind warns when its own session reads the switch otherwise than the
/// database, or when a value is neither <c>on</c> nor <c>off</c>.</para>
/// <para>It runs in <see cref="StartingAsync"/>, before any hosted service starts. A database it cannot reach is
/// logged and not refused: the check cannot be made, and the host's other work does not depend on it.</para>
/// </remarks>
internal sealed class ChangeFeedStartupValidator(
    StateMachineOptions options,
    ISnapshotNoticeSource? notices,
    ILogger<ChangeFeedStartupValidator>? logger = null
) : IHostedLifecycleService
{
    private readonly ILogger _logger = logger ?? NullLogger<ChangeFeedStartupValidator>.Instance;
    private bool _checked;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        if (notices is null)
            return;

        SnapshotNoticeSwitch read;
        try
        {
            read = await notices.ReadSwitchAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not read whether the database sends state-machine change notices; the change feed ({Feed}) "
                    + "is not checked against it.",
                options.ChangeFeed
            );
            return;
        }

        if (
            options.ChangeFeed == StateMachineChangeFeed.Notify
            && Silent(read) is { Count: > 0 } why
        )
            throw new InvalidOperationException(
                "StateMachineOptions.ChangeFeed is Notify, but "
                    + string.Join("; ", why)
                    + ". A write that sends no notice is never pushed to a watcher. The trigger reads "
                    + "trax.snapshot_notify in the session that writes, so the only supported switch is the "
                    + "database's own (ALTER DATABASE <name> SET trax.snapshot_notify), and only once every host "
                    + "polls. Set ChangeFeed = StateMachineChangeFeed.Poll on every host, or turn the notices back on "
                    + "(ALTER DATABASE <name> RESET trax.snapshot_notify, and RESET it for each role and connection "
                    + "string that sets it)."
            );

        if (options.ChangeFeed == StateMachineChangeFeed.Poll && read.DatabaseSends)
            _logger.LogWarning(
                "StateMachineOptions.ChangeFeed is Poll, but the database still sends a change notice for every "
                    + "write to trax.snapshot_draft, so writes still pay for it. Once every host polls, turn the "
                    + "notices off: ALTER DATABASE <name> SET trax.snapshot_notify = 'off'."
            );

        if (
            read.TriggerFires
            && read.TriggerHasCondition
            && read.SessionSends != read.DatabaseSends
        )
            _logger.LogWarning(
                "This host's session reads trax.snapshot_notify as {Session}, but the database's own setting is "
                    + "{Database}, so this host's writes {Effect} while the database's {DatabaseEffect}. Only the "
                    + "database's setting is supported: remove the one this host's role or connection string sets.",
                Shown(read.SessionValue),
                Shown(read.DatabaseValue),
                read.SessionSends ? "send notices" : "send none",
                read.DatabaseSends ? "send them" : "send none"
            );

        foreach (
            var value in new[] { read.DatabaseValue, read.SessionValue }
                .Where(v => !SnapshotNoticeSwitch.IsKnown(v))
                .Distinct(StringComparer.Ordinal)
        )
            _logger.LogWarning(
                "trax.snapshot_notify is '{Value}', which is neither 'on' nor 'off', so it keeps the state-machine "
                    + "change notices on. Write 'on' or 'off'.",
                value
            );
    }

    // Why some write would send no notice, as far as this host can tell; empty when every write it can see sends one.
    private static List<string> Silent(SnapshotNoticeSwitch read)
    {
        if (!read.TriggerFires)
            return
            [
                read.TriggerState is null
                    ? "the trigger snapshot_draft_changed_notify on trax.snapshot_draft is missing"
                    : $"the trigger snapshot_draft_changed_notify on trax.snapshot_draft is not enabled for "
                        + $"ordinary sessions (tgenabled = '{read.TriggerState}'; it must be 'O' or 'A')",
            ];

        var why = new List<string>();
        if (read.TriggerHasCondition && SnapshotNoticeSwitch.IsOff(read.DatabaseValue))
            why.Add(
                $"the database turns the change notices off (trax.snapshot_notify = '{read.DatabaseValue}')"
            );
        if (read.TriggerHasCondition && read.RolesTurningOff.Count > 0)
            why.Add(
                "the role(s) "
                    + string.Join(", ", read.RolesTurningOff)
                    + " turn the change notices off for their own sessions (ALTER ROLE ... SET trax.snapshot_notify)"
            );
        if (!read.SessionSends && why.Count == 0)
            why.Add(
                "this host's own sessions send no change notices (its role or connection string sets "
                    + "trax.snapshot_notify off, or session_replication_role is replica)"
            );
        return why;
    }

    private static string Shown(string? value) => value is null ? "unset" : $"'{value}'";

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
