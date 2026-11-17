using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>The steps a timer test drives on one host of an <see cref="InvokeCluster"/>.</summary>
internal static class TimerSteps
{
    /// <summary>One pass of this host's draft sweep, which deletes the drafts that have expired; how many it deleted.</summary>
    public static Task<int> ExpireDrafts(this ClusterHost host) =>
        host.Services.GetRequiredService<DraftSweeper>().SweepOnce();

    /// <summary>One sweep of due timers on this host.</summary>
    public static Task<IReadOnlyList<TimerFireResult>> FireTimers(this ClusterHost host) =>
        host.Reconciler.FireDueTimersOnce(CancellationToken.None);

    /// <summary>
    /// Creates <paramref name="user"/>'s reminder in <c>Idle</c> as host code does, through the public store (a machine
    /// with a rule or a timer moves through its declared events only, so no save creates it), and opens it into
    /// <c>Review</c> as an editor.
    /// </summary>
    public static async Task<AdvanceOutcome> OpenReview(
        this ClusterHost host,
        string user,
        Guid id,
        int version = 1
    )
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var created = await scope
            .ServiceProvider.GetRequiredService<ISnapshotStore>()
            .Upsert(
                user,
                id,
                new Snapshot
                {
                    Machine = ReminderMachine.MachineId,
                    Version = version,
                    State = nameof(ReminderState.Idle),
                    Context = ReminderMachine.Context(),
                }
            )
            .ConfigureAwait(false);
        if (!created)
            throw new InvalidOperationException("The reminder could not be created.");
        return await Reminders(scope)
            .Advance(new Actor.Client(user, "Editor"), id, nameof(ReminderTrigger.Open))
            .ConfigureAwait(false);
    }

    public static async Task<AdvanceOutcome> Send(
        this ClusterHost host,
        string user,
        Guid id,
        ReminderTrigger trigger,
        string? requestId = null
    )
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await Reminders(scope)
            .Advance(user, id, trigger.ToString(), requestId: requestId)
            .ConfigureAwait(false);
    }

    public static async Task<AutosaveResult> Save(
        this ClusterHost host,
        string user,
        Guid id,
        string json
    )
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await Reminders(scope).Autosave(user, id, json).ConfigureAwait(false);
    }

    public static async Task<LoadResult> Load(this ClusterHost host, string user, Guid id)
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await Reminders(scope).Load(user, id).ConfigureAwait(false);
    }

    /// <summary>Runs the reminder's effect for <paramref name="user"/>'s draft, as a send does.</summary>
    public static async Task<AdvanceOutcome> Publish(
        this ClusterHost host,
        string user,
        Guid id,
        string requestId
    )
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .EffectRunner(ReminderMachine.MachineId)
            .NotNull()
            .Run(user, id, requestId)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Discards <paramref name="user"/>'s reminder as its owner does, which releases its effect claim with it, so a
    /// reminder created again under the id is a new intent.
    /// </summary>
    public static async Task Delete(this ClusterHost host, string user, Guid id)
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var discarded = await Reminders(scope)
            .Discard(new Actor.Client(user), id, "discard-" + Guid.NewGuid().ToString("N"))
            .ConfigureAwait(false);
        if (discarded is not DiscardOutcome.Discarded)
            throw new InvalidOperationException(
                $"The reminder could not be discarded: {discarded}"
            );
    }

    /// <summary>Starts a system-owned <see cref="WaitMachine"/> instance with <paramref name="context"/>.</summary>
    public static Task<MachineInstance> StartWait(this ClusterHost host, JsonObject context) =>
        host.StartSystem<WaitMachine>(context);

    public static async Task<MachineInstance> StartSystem<TMachine>(
        this ClusterHost host,
        JsonObject context
    )
        where TMachine : IMachine
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Start<TMachine>(MachineKey.Of("timer", Guid.NewGuid().ToString("N")), context)
            .ConfigureAwait(false);
    }

    /// <summary>Every row of <paramref name="machine"/> under <paramref name="id"/>, whoever owns it.</summary>
    public static async Task<List<SnapshotDraft>> Rows(
        this ClusterHost host,
        Guid id,
        string machine
    )
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.Id == id && d.Machine == machine)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Moves a row's <c>updated_at</c> back by <paramref name="by"/>, as if it had been idle that long.</summary>
    public static async Task IdleDraft(this ClusterHost host, long rowId, TimeSpan by)
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var at = DateTimeOffset.UtcNow - by;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(d => d.RowId == rowId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, at))
            .ConfigureAwait(false);
    }

    /// <summary>Moves a row's <c>created_at</c> back by <paramref name="by"/>, as if it had been made that long ago.</summary>
    public static async Task AgeDraft(this ClusterHost host, long rowId, TimeSpan by)
    {
        var scope = host.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        DateTimeOffset? at = DateTimeOffset.UtcNow - by;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(d => d.RowId == rowId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedAt, at))
            .ConfigureAwait(false);
    }

    private static ISnapshotDraftService Reminders(IServiceScope scope) =>
        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service(ReminderMachine.MachineId)
            .NotNull();
}
