using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.MachinePolicyFire;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// Fires what the machine policies owe (<c>trax.machine_policy_fire</c>, ADR <c>Trax.Docs/adr/0058</c>). The server
/// acts, as a timer does: each fire is a server-only trigger on one instance, written with actor <c>Policy</c>, never as
/// the owner and never with a role. Every row is worked in a transaction of its own and deleted in it, so a fire happens
/// once whichever host claims it; a row another host holds is passed over (<c>FOR UPDATE SKIP LOCKED</c> on Postgres; on
/// SQLite the transaction holds the database's write lock). A row that cannot be fired is logged and dropped, and never
/// blocks the rest. A row whose target another write holds, or that has an effect in flight, is kept and tried again on
/// a later pass.
/// </summary>
/// <remarks>
/// A row with no target is one policy's reaction to one transition. A linked policy fires on the source owner's
/// instances whose context holds the source's id at the policy's link field, a system policy on the instance at its
/// key: those fire in the same transaction. A broadcast is expanded into one row per instance waiting in its state, at
/// most <see cref="PolicyRunnerOptions.FanOutCap"/>, and every other unexpanded row of the same policy that no other host
/// holds is coalesced into it, since they would reach the same instances. Its rows are then fired <see cref="PolicyRunnerOptions.BatchSize"/>
/// at a time.
/// </remarks>
internal sealed class MachinePolicyRunner(
    IServiceScopeFactory scopes,
    MachinePolicies policies,
    IEnumerable<IMachine> machines,
    PolicyRunnerOptions options,
    ILogger<MachinePolicyRunner> logger
) : BackgroundService
{
    private readonly Lazy<IReadOnlyDictionary<string, IMachineInternals>> _byName = new(() =>
        machines
            .OfType<IMachineInternals>()
            .GroupBy(m => ((IMachine)m).Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await PassOnce(stoppingToken) > 0) { }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "A pass over the machine policies' fires failed; the next pass tries again."
                );
            }
            try
            {
                await Task.Delay(options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: expands what is owed, then fires one batch. Returns how many rows it worked.</summary>
    internal async Task<int> PassOnce(CancellationToken cancellationToken)
    {
        List<MachinePolicyFire> owed;
        await using (var scope = scopes.CreateAsyncScope())
            owed = await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .MachinePolicyFires.AsNoTracking()
                .OrderBy(f => f.Id)
                .Take(options.BatchSize)
                .ToListAsync(cancellationToken);

        var worked = 0;
        foreach (var row in owed)
            try
            {
                if (await Work(row, cancellationToken))
                    worked++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "The policy fire {Row} ({Policy}) failed and is tried again.",
                    row.Id,
                    row.Policy
                );
            }
        return worked;
    }

    // One row, in its own transaction: expanded or fired, then deleted. False when another host holds it, it is gone, or
    // its target is busy, so it is kept for a later pass.
    private async Task<bool> Work(MachinePolicyFire row, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IDataContext>();
        using var transaction = await context.BeginTransaction(cancellationToken);
        if (!await Lock(context, row.Id, cancellationToken))
            return false;

        if (!policies.ById.TryGetValue(row.Policy, out var policy))
        {
            logger.LogWarning(
                "The policy fire {Row} names {Policy}, which this host does not serve; it is dropped.",
                row.Id,
                row.Policy
            );
            await Delete(context, row.Id, cancellationToken);
        }
        else if (row.TargetRowId is { } target)
        {
            if (!await Fire(services, context, policy, target, cancellationToken))
                return false;
            await Delete(context, row.Id, cancellationToken);
        }
        else if (policy.Shape == "broadcast")
            await Expand(context, row, policy, cancellationToken);
        else
        {
            var busy = new List<long>();
            foreach (var found in await Targets(context, row, policy, cancellationToken))
                if (!await Fire(services, context, policy, found, cancellationToken))
                    busy.Add(found);
            // A busy target gets a row of its own, so a later pass fires it and the others are not fired twice.
            if (busy.Count > 0)
                await Owe(context, row, busy, cancellationToken);
            await Delete(context, row.Id, cancellationToken);
        }
        await transaction.Commit();
        return true;
    }

    // A broadcast's targets, each a row of its own, and this policy's other unexpanded rows coalesced into them. A row
    // another host holds is left to it: waiting on it while that host waits on this row would deadlock.
    private async Task Expand(
        IDataContext context,
        MachinePolicyFire row,
        HostPolicy policy,
        CancellationToken cancellationToken
    )
    {
        var waiting = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.Machine == policy.Target && d.State == policy.By)
            .OrderBy(d => d.RowId)
            .Select(d => d.RowId)
            .Take(options.FanOutCap + 1)
            .ToListAsync(cancellationToken);
        if (waiting.Count > options.FanOutCap)
        {
            logger.LogWarning(
                "The policy {Policy} reaches more than {Cap} instances of {Machine} in {State}; the first {Cap} are fired and the rest are not.",
                row.Policy,
                options.FanOutCap,
                policy.Target,
                policy.By,
                options.FanOutCap
            );
            waiting.RemoveAt(waiting.Count - 1);
        }
        var coalesced = await LockUnexpanded(context, row.Policy, cancellationToken);
        await context
            .MachinePolicyFires.Where(f => coalesced.Contains(f.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await Owe(context, row, waiting, cancellationToken);
    }

    // One row of the policy per target, from the same source.
    private static async Task Owe(
        IDataContext context,
        MachinePolicyFire row,
        IEnumerable<long> targets,
        CancellationToken cancellationToken
    )
    {
        var now = DateTimeOffset.UtcNow;
        context.MachinePolicyFires.AddRange(
            targets.Select(target => new MachinePolicyFire
            {
                Policy = row.Policy,
                SourceRowId = row.SourceRowId,
                SourceInstanceId = row.SourceInstanceId,
                SourceRevision = row.SourceRevision,
                TargetRowId = target,
                CreatedAt = now,
            })
        );
        await context.SaveChanges(cancellationToken);
    }

    // A linked policy's targets: the source owner's instances of the target machine whose link field holds the source's
    // id. A system policy's: the instance at its key. None when the source is gone or is not a user's.
    private static async Task<IReadOnlyList<long>> Targets(
        IDataContext context,
        MachinePolicyFire row,
        HostPolicy policy,
        CancellationToken cancellationToken
    )
    {
        if (policy.Shape == "toSystem")
        {
            var id = SystemInstanceStarter.IdOf(policy.Target, policy.By);
            return await context
                .SnapshotDrafts.AsNoTracking()
                .Where(d =>
                    d.Id == id
                    && d.Machine == policy.Target
                    && d.OwnerKind == SnapshotOwnerKind.System
                )
                .Select(d => d.RowId)
                .ToListAsync(cancellationToken);
        }

        var source = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.RowId == row.SourceRowId)
            .Select(d => new { d.OwnerKind, d.UserKey })
            .FirstOrDefaultAsync(cancellationToken);
        if (source is not { OwnerKind: SnapshotOwnerKind.User, UserKey: { } owner })
            return [];
        var candidates = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.Machine == policy.Target
                && d.OwnerKind == SnapshotOwnerKind.User
                && d.UserKey == owner
            )
            .Select(d => new { d.RowId, d.Context })
            .ToListAsync(cancellationToken);
        return candidates
            .Where(c =>
                JsonNode.Parse(c.Context)?[policy.By] is JsonValue value
                && value.TryGetValue<string>(out var linked)
                && Guid.TryParse(linked, out var linkedId)
                && linkedId == row.SourceInstanceId
            )
            .Select(c => c.RowId)
            .ToList();
    }

    // Fires the policy's trigger on one instance, as the server: the engine's advance, then the timer's write path,
    // which starts and cancels runs and timers as the state is entered and left. True when the fire is done with,
    // fired or never to be; false when the target is busy and the fire is owed still.
    private async Task<bool> Fire(
        IServiceProvider services,
        IDataContext context,
        HostPolicy policy,
        long target,
        CancellationToken cancellationToken
    )
    {
        var fired = await TryFire(services, context, policy, target, cancellationToken);
        if (fired is ServerFire.Fired.Busy busy)
        {
            logger.LogDebug(
                "The policy {Policy} did not fire {Trigger} on instance row {Target} yet: {Why}; a later pass tries again.",
                policy.When + "." + policy.Enters,
                policy.Trigger,
                target,
                busy.Why
            );
            return false;
        }
        var failure = fired switch
        {
            ServerFire.Fired.Moved => null,
            ServerFire.Fired.Refused refused => $"{refused.Code.ToWire()}: {refused.Message}",
            ServerFire.Fired.Failed failed => failed.Why,
            _ => "unknown result",
        };
        if (failure is not null)
            logger.LogWarning(
                "The policy {Policy} did not fire {Trigger} on instance row {Target}: {Failure}.",
                policy.When + "." + policy.Enters,
                policy.Trigger,
                target,
                failure
            );
        return true;
    }

    // The fire itself.
    private Task<ServerFire.Fired> TryFire(
        IServiceProvider services,
        IDataContext context,
        HostPolicy policy,
        long target,
        CancellationToken cancellationToken
    ) =>
        ServerFire.Fire(
            services,
            context,
            _byName.Value,
            target,
            policy.Trigger,
            null,
            TransitionNote.Policy(policy.Trigger),
            state =>
                policy.Shape == "broadcast" && state != policy.By
                    ? $"it left {policy.By} before the policy reached it"
                    : null,
            cancellationToken
        );

    // Claims a fire row for this transaction: on Postgres a row another host holds is passed over, not waited on.
    private static async Task<bool> Lock(
        IDataContext context,
        long id,
        CancellationToken cancellationToken
    )
    {
        var database = ((DbContext)context).Database;
        if (database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            return (
                await database
                    .SqlQueryRaw<long>(
                        """
                        SELECT id AS "Value" FROM trax.machine_policy_fire
                        WHERE id = {0}
                        FOR UPDATE SKIP LOCKED
                        """,
                        id
                    )
                    .ToListAsync(cancellationToken)
            )
                is [_];
        return await context
            .MachinePolicyFires.AsNoTracking()
            .AnyAsync(f => f.Id == id, cancellationToken);
    }

    // The policy's unexpanded rows this transaction can claim, its own among them: on Postgres a row another host
    // holds is passed over; on SQLite the transaction's write lock covers them all.
    private static async Task<List<long>> LockUnexpanded(
        IDataContext context,
        string policy,
        CancellationToken cancellationToken
    )
    {
        var database = ((DbContext)context).Database;
        if (database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            return await database
                .SqlQueryRaw<long>(
                    """
                    SELECT id AS "Value" FROM trax.machine_policy_fire
                    WHERE policy = {0} AND target_row_id IS NULL
                    FOR UPDATE SKIP LOCKED
                    """,
                    policy
                )
                .ToListAsync(cancellationToken);
        return await context
            .MachinePolicyFires.AsNoTracking()
            .Where(f => f.Policy == policy && f.TargetRowId == null)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
    }

    private static Task<int> Delete(
        IDataContext context,
        long id,
        CancellationToken cancellationToken
    ) => context.MachinePolicyFires.Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken);
}
