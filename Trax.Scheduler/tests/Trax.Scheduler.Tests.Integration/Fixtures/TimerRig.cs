using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>What a timer test registers on an <see cref="InvokeCluster"/> host, beyond the cluster's defaults.</summary>
internal static class TimerHosts
{
    /// <summary>Registers the reminder's effect, which every host that runs a reminder's <c>Publish</c> needs.</summary>
    public static void Publishing(IServiceCollection services) =>
        services.AddSingleton<IReminderPublish, ReminderPublish>();

    /// <summary>Keeps only <paramref name="machines"/> of the machines the assembly scan registered.</summary>
    public static Action<IServiceCollection> Only(params Type[] machines) =>
        services =>
        {
            foreach (
                var descriptor in services
                    .Where(d =>
                        d.ServiceType == typeof(IMachine)
                        && !machines.Contains(d.ImplementationType)
                    )
                    .ToList()
            )
                services.Remove(descriptor);
        };

    /// <summary>Drops <paramref name="machines"/> from the machines the assembly scan registered.</summary>
    public static Action<IServiceCollection> Without(params Type[] machines) =>
        services =>
        {
            foreach (
                var descriptor in services
                    .Where(d =>
                        d.ServiceType == typeof(IMachine) && machines.Contains(d.ImplementationType)
                    )
                    .ToList()
            )
                services.Remove(descriptor);
        };

    /// <summary>Runs the reminder as <paramref name="shape"/> on this host instead of version 1.</summary>
    public static Action<IServiceCollection> Deploy(ReminderShape shape) =>
        services =>
        {
            Without(typeof(ReminderMachine))(services);
            services.AddSingleton<IMachine>(new ReminderMachine(shape));
        };

    /// <summary>Holds the next draft write on this host while <paramref name="gate"/> is armed.</summary>
    public static Action<IServiceCollection> Pausing(PauseGate gate) =>
        services => PausingSnapshotStore.Install(services, gate);

    /// <summary>Every one of <paramref name="parts"/>, in order.</summary>
    public static Action<IServiceCollection> All(params Action<IServiceCollection>[] parts) =>
        services =>
        {
            foreach (var part in parts)
                part(services);
        };
}

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
        using var scope = host.Services.CreateScope();
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
            );
        if (!created)
            throw new InvalidOperationException("The reminder could not be created.");
        return await Reminders(scope)
            .Advance(new Actor.Client(user, "Editor"), id, nameof(ReminderTrigger.Open));
    }

    public static async Task<AdvanceOutcome> Send(
        this ClusterHost host,
        string user,
        Guid id,
        ReminderTrigger trigger,
        string? requestId = null
    )
    {
        using var scope = host.Services.CreateScope();
        return await Reminders(scope).Advance(user, id, trigger.ToString(), requestId: requestId);
    }

    public static async Task<AutosaveResult> Save(
        this ClusterHost host,
        string user,
        Guid id,
        string json
    )
    {
        using var scope = host.Services.CreateScope();
        return await Reminders(scope).Autosave(user, id, json);
    }

    public static async Task<LoadResult> Load(this ClusterHost host, string user, Guid id)
    {
        using var scope = host.Services.CreateScope();
        return await Reminders(scope).Load(user, id);
    }

    /// <summary>Runs the reminder's effect for <paramref name="user"/>'s draft, as a send does.</summary>
    public static async Task<AdvanceOutcome> Publish(
        this ClusterHost host,
        string user,
        Guid id,
        string requestId
    )
    {
        using var scope = host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .EffectRunner(ReminderMachine.MachineId)!
            .Run(user, id, requestId);
    }

    /// <summary>
    /// Discards <paramref name="user"/>'s reminder as its owner does, which releases its effect claim with it, so a
    /// reminder created again under the id is a new intent.
    /// </summary>
    public static async Task Delete(this ClusterHost host, string user, Guid id)
    {
        using var scope = host.Services.CreateScope();
        var discarded = await Reminders(scope)
            .Discard(new Actor.Client(user), id, "discard-" + Guid.NewGuid().ToString("N"));
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
        using var scope = host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Start<TMachine>(MachineKey.Of("timer", Guid.NewGuid().ToString("N")), context);
    }

    /// <summary>Every row of <paramref name="machine"/> under <paramref name="id"/>, whoever owns it.</summary>
    public static async Task<List<SnapshotDraft>> Rows(
        this ClusterHost host,
        Guid id,
        string machine
    )
    {
        using var scope = host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.Id == id && d.Machine == machine)
            .ToListAsync();
    }

    /// <summary>Moves a row's <c>updated_at</c> back by <paramref name="by"/>, as if it had been idle that long.</summary>
    public static async Task IdleDraft(this ClusterHost host, long rowId, TimeSpan by)
    {
        using var scope = host.Services.CreateScope();
        var at = DateTimeOffset.UtcNow - by;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(d => d.RowId == rowId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, at));
    }

    /// <summary>Moves a row's <c>created_at</c> back by <paramref name="by"/>, as if it had been made that long ago.</summary>
    public static async Task AgeDraft(this ClusterHost host, long rowId, TimeSpan by)
    {
        using var scope = host.Services.CreateScope();
        DateTimeOffset? at = DateTimeOffset.UtcNow - by;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(d => d.RowId == rowId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedAt, at));
    }

    private static ISnapshotDraftService Reminders(IServiceScope scope) =>
        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service(ReminderMachine.MachineId)!;
}

/// <summary>
/// Reads and writes the timer columns of <c>snapshot_draft</c> by raw SQL, on the cluster's own database. Time
/// moves by writing <c>due_at</c>: a test makes a timer due by backdating it, never by waiting. On Postgres the
/// database's clock is the one read; SQLite has no clock of its own, so there it is the test's.
/// </summary>
internal sealed class TimerDb(InvokeCluster cluster)
{
    private bool Postgres => cluster.Store == ClusterStore.Postgres;

    private string Table => Postgres ? "trax.snapshot_draft" : "snapshot_draft";

    /// <summary>Makes the row's timer due: <c>due_at</c> is <paramref name="ago"/> (one second by default) in the past.</summary>
    public Task MakeDue(long rowId, TimeSpan? ago = null) =>
        DueIn(rowId, -(ago ?? TimeSpan.FromSeconds(1)));

    /// <summary>Sets the row's <c>due_at</c> to <paramref name="from"/> from now on the database's clock.</summary>
    public Task DueIn(long rowId, TimeSpan from) =>
        Postgres
            ? Execute(
                $"UPDATE {Table} SET due_at = now() + @from WHERE row_id = @row",
                ("from", from),
                ("row", rowId)
            )
            : Execute(
                $"UPDATE {Table} SET due_at = @at WHERE row_id = @row",
                ("at", DateTimeOffset.UtcNow + from),
                ("row", rowId)
            );

    /// <summary>Makes every row of <paramref name="machine"/> due, one second ago.</summary>
    public Task MakeAllDue(string machine) =>
        Postgres
            ? Execute(
                $"UPDATE {Table} SET due_at = now() - interval '1 second' WHERE machine = @machine",
                ("machine", machine)
            )
            : Execute(
                $"UPDATE {Table} SET due_at = @at WHERE machine = @machine",
                ("at", DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1)),
                ("machine", machine)
            );

    /// <summary>The row's <c>due_at</c>, or <c>null</c> when it has no timer pending.</summary>
    public async Task<DateTimeOffset?> DueAt(long rowId)
    {
        await using var connection = await Open();
        await using var command = Command(
            connection,
            $"SELECT due_at FROM {Table} WHERE row_id = @row",
            ("row", rowId)
        );
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"No row {rowId}.");
        if (await reader.IsDBNullAsync(0))
            return null;
        return Postgres
            ? reader.GetFieldValue<DateTimeOffset>(0)
            : DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
    }

    /// <summary>The row's revision.</summary>
    public async Task<long> Revision(long rowId)
    {
        await using var connection = await Open();
        await using var command = Command(
            connection,
            $"SELECT revision FROM {Table} WHERE row_id = @row",
            ("row", rowId)
        );
        return Convert.ToInt64(
            await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException($"No row {rowId}."),
            CultureInfo.InvariantCulture
        );
    }

    /// <summary>Now, on the clock <c>due_at</c> is written by.</summary>
    public async Task<DateTimeOffset> Now()
    {
        if (!Postgres)
            return DateTimeOffset.UtcNow;
        await using var connection = await Open();
        await using var command = Command(connection, "SELECT now()");
        // Npgsql reads a timestamptz scalar as a UTC DateTime.
        return (await command.ExecuteScalarAsync()) switch
        {
            DateTimeOffset offset => offset,
            DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
            var other => throw new InvalidOperationException($"now() came back as {other}."),
        };
    }

    /// <summary>Overwrites the row's context, as a host write that does not re-enter the state would.</summary>
    public Task SetContext(long rowId, JsonObject context) =>
        Execute(
            Postgres
                ? $"UPDATE {Table} SET context = CAST(@context AS jsonb) WHERE row_id = @row"
                : $"UPDATE {Table} SET context = @context WHERE row_id = @row",
            ("context", context.ToJsonString()),
            ("row", rowId)
        );

    private async Task Execute(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Open();
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<DbConnection> Open()
    {
        DbConnection connection = Postgres
            ? new NpgsqlConnection(cluster.ConnectionString)
            : new SqliteConnection(cluster.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static DbCommand Command(
        DbConnection connection,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
