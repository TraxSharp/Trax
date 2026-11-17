using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

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

    /// <summary>The row's <c>due_at</c>, or <see langword="null"/> when it has no timer pending.</summary>
    public async Task<DateTimeOffset?> DueAt(long rowId)
    {
        var connection = await Open().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = Command(
                connection,
                $"SELECT due_at FROM {Table} WHERE row_id = @row",
                ("row", rowId)
            );
            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync().ConfigureAwait(false))
                        throw new InvalidOperationException(
                            string.Create(CultureInfo.InvariantCulture, $"No row {rowId}.")
                        );
                    if (await reader.IsDBNullAsync(0).ConfigureAwait(false))
                        return null;
                    return Postgres
                        ? await reader.GetFieldValueAsync<DateTimeOffset>(0).ConfigureAwait(false)
                        : DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
                }
            }
        }
    }

    /// <summary>The row's revision.</summary>
    public async Task<long> Revision(long rowId)
    {
        var connection = await Open().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = Command(
                connection,
                $"SELECT revision FROM {Table} WHERE row_id = @row",
                ("row", rowId)
            );
            await using (command.ConfigureAwait(false))
            {
                return Convert.ToInt64(
                    await command.ExecuteScalarAsync().ConfigureAwait(false)
                        ?? throw new InvalidOperationException(
                            string.Create(CultureInfo.InvariantCulture, $"No row {rowId}.")
                        ),
                    CultureInfo.InvariantCulture
                );
            }
        }
    }

    /// <summary>Now, on the clock <c>due_at</c> is written by.</summary>
    public async Task<DateTimeOffset> Now()
    {
        if (!Postgres)
            return DateTimeOffset.UtcNow;
        var connection = await Open().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = Command(connection, "SELECT now()");
            await using (command.ConfigureAwait(false))
            {
                // Npgsql reads a timestamptz scalar as a UTC DateTime.
                return (await command.ExecuteScalarAsync().ConfigureAwait(false)) switch
                {
                    DateTimeOffset offset => offset,
                    DateTime time => new DateTimeOffset(
                        DateTime.SpecifyKind(time, DateTimeKind.Utc)
                    ),
                    var other => throw new InvalidOperationException(
                        $"now() came back as {other}."
                    ),
                };
            }
        }
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
        var connection = await Open().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = Command(connection, sql, parameters);
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<DbConnection> Open()
    {
        DbConnection connection = Postgres
            ? new NpgsqlConnection(cluster.ConnectionString)
            : new SqliteConnection(cluster.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
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
