using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>A draft row's timer columns as the database holds them, read by raw SQL.</summary>
/// <param name="State">The state column.</param>
/// <param name="DueAt">The <c>due_at</c> column, in UTC.</param>
/// <param name="Revision">The <c>revision</c> column.</param>
public sealed record TimerRow(string State, DateTimeOffset? DueAt, long Revision);

/// <summary>
/// Raw SQL over a host's own database, on Postgres and SQLite alike, for the columns a test reads that the data
/// context does not map (yet), and for writes that move time by backdating a column.
/// </summary>
internal static class RawSql
{
    /// <summary>The draft table's name on the host's provider.</summary>
    public static string Table(InstanceHost host) =>
        host.Provider == StoreProvider.Postgres ? "trax.snapshot_draft" : "snapshot_draft";

    /// <summary>The timer columns of the one row under <paramref name="id"/>, or null when there is none.</summary>
    public static async Task<TimerRow?> Timer(InstanceHost host, Guid id)
    {
        var rows = await Query(
            host,
            $"SELECT state, due_at, revision FROM {Table(host)} WHERE id = @id",
            ("@id", Id(host, id))
        );
        return rows.Count switch
        {
            0 => null,
            1 => new TimerRow(
                (string)rows[0][0]!,
                AsInstant(rows[0][1]),
                Convert.ToInt64(rows[0][2], CultureInfo.InvariantCulture)
            ),
            _ => throw new InvalidOperationException($"{rows.Count} rows hold the id {id}."),
        };
    }

    /// <summary>The database's clock, in UTC.</summary>
    public static async Task<DateTimeOffset> Now(InstanceHost host)
    {
        var rows = await Query(
            host,
            host.Provider == StoreProvider.Postgres
                ? "SELECT now()"
                : "SELECT strftime('%Y-%m-%d %H:%M:%f', 'now')"
        );
        return AsInstant(rows[0][0])
            ?? throw new InvalidOperationException("The database returned no time.");
    }

    /// <summary>Moves the row's timer to <paramref name="dueAt"/>, as a test does instead of waiting for it.</summary>
    public static Task<int> SetDueAt(InstanceHost host, Guid id, DateTimeOffset dueAt) =>
        Execute(
            host,
            $"UPDATE {Table(host)} SET due_at = @due WHERE id = @id",
            (
                "@due",
                host.Provider == StoreProvider.Postgres ? dueAt.ToUniversalTime() : Text(dueAt)
            ),
            ("@id", Id(host, id))
        );

    /// <summary>
    /// The effect pending on the one row under <paramref name="id"/>: its claimant's token and when its lease ends, or
    /// nulls when none is pending.
    /// </summary>
    public static async Task<(Guid? Token, DateTimeOffset? LeaseUntil)> PendingEffect(
        InstanceHost host,
        Guid id
    )
    {
        var rows = await Query(
            host,
            $"SELECT effect_token, effect_lease_until FROM {Table(host)} WHERE id = @id",
            ("@id", Id(host, id))
        );
        var row = rows.Single();
        return (
            row[0] switch
            {
                null => null,
                Guid guid => guid,
                string text => Guid.Parse(text),
                var other => throw new InvalidOperationException(
                    $"effect_token came back as a {other.GetType()}."
                ),
            },
            AsInstant(row[1])
        );
    }

    /// <summary>Moves the lease of the effect pending on the row to <paramref name="until"/>, as a test does instead of waiting.</summary>
    public static Task<int> SetEffectLease(InstanceHost host, Guid id, DateTimeOffset until) =>
        Execute(
            host,
            $"UPDATE {Table(host)} SET effect_lease_until = @until WHERE id = @id AND effect_token IS NOT NULL",
            ("@until", Instant(host, until)),
            ("@id", Id(host, id))
        );

    /// <summary>
    /// Marks the row as if <paramref name="trigger"/>'s effect were pending on it, claimed by a host that stopped:
    /// a fresh token, the lease ending at <paramref name="leaseUntil"/>, and the request that sent it.
    /// </summary>
    public static Task<int> MarkEffect(
        InstanceHost host,
        Guid id,
        string trigger,
        string requestId,
        DateTimeOffset leaseUntil
    ) =>
        Execute(
            host,
            $"UPDATE {Table(host)} SET effect_token = @token, effect_trigger = @trigger, "
                + "effect_request_id = @request, effect_lease_until = @until WHERE id = @id",
            ("@token", Id(host, Guid.NewGuid())),
            ("@trigger", trigger),
            ("@request", requestId),
            ("@until", Instant(host, leaseUntil)),
            ("@id", Id(host, id))
        );

    /// <summary>Whether the row records that the server put it in its state: true, false, or null for a row from before the record.</summary>
    public static async Task<bool?> ServerRecord(InstanceHost host, Guid id)
    {
        var value = (
            await Query(
                host,
                $"SELECT server_record FROM {Table(host)} WHERE id = @id",
                ("@id", Id(host, id))
            )
        ).Single()[0];
        return value is null ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Sets the row's server record, null as a row written before the record existed has it.</summary>
    public static Task<int> SetServerRecord(InstanceHost host, Guid id, bool? value) =>
        Execute(
            host,
            $"UPDATE {Table(host)} SET server_record = @value WHERE id = @id",
            ("@value", value),
            ("@id", Id(host, id))
        );

    private static object Instant(InstanceHost host, DateTimeOffset instant) =>
        host.Provider == StoreProvider.Postgres ? instant.ToUniversalTime() : Text(instant);

    /// <summary>Runs <paramref name="sql"/> and returns how many rows it changed.</summary>
    public static async Task<int> Execute(
        InstanceHost host,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using var scope = host.Scope();
        var db = (DbContext)scope.ServiceProvider.GetRequiredService<IDataContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = Command(db.Database.GetDbConnection(), sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs <paramref name="sql"/> and returns each row's values in column order.</summary>
    public static async Task<List<object?[]>> Query(
        InstanceHost host,
        string sql,
        params (string Name, object? Value)[] parameters
    )
    {
        using var scope = host.Scope();
        var db = (DbContext)scope.ServiceProvider.GetRequiredService<IDataContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = Command(db.Database.GetDbConnection(), sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                values[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(values);
        }
        return rows;
    }

    /// <summary>A column value read back as an instant in UTC: a Postgres timestamp, or SQLite's text form.</summary>
    public static DateTimeOffset? AsInstant(object? value) =>
        value switch
        {
            null => null,
            DateTimeOffset offset => offset.ToUniversalTime(),
            DateTime time => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)),
            string text => DateTimeOffset.Parse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
            ),
            _ => throw new InvalidOperationException(
                $"A time column came back as a {value.GetType()}."
            ),
        };

    // SQLite stores a Guid as its uppercase text form under EF's default converter; Postgres as a uuid.
    private static object Id(InstanceHost host, Guid id) =>
        host.Provider == StoreProvider.Postgres ? id : id.ToString().ToUpperInvariant();

    private static string Text(DateTimeOffset instant) =>
        instant
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    private static DbCommand Command(
        DbConnection connection,
        string sql,
        (string Name, object? Value)[] parameters
    )
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }
}
