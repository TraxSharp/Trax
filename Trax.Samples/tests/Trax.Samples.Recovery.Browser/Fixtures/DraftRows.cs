using Npgsql;
using Trax.Samples.Recovery.Browser.Factories;

namespace Trax.Samples.Recovery.Browser.Fixtures;

/// <summary>
/// What the host stored for a topic map instance, read from <c>trax.snapshot_draft</c> as the test's own
/// witness: the page shows what the server sent it, and this shows what the server holds.
/// </summary>
public static class DraftRows
{
    /// <summary>The instance whose id starts with <paramref name="prefix"/>, the part of it the page shows.</summary>
    public static async Task<(Guid Id, long RowId, string State)> FindAsync(string prefix)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id, row_id, state FROM trax.snapshot_draft
            WHERE machine = 'topic-map' AND id::text LIKE @prefix || '%'
            ORDER BY updated_at DESC LIMIT 1
            """,
            connection
        );
        command.Parameters.AddWithValue("prefix", prefix);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException(
                $"No topic-map instance has an id starting {prefix}."
            );
        return (reader.GetGuid(0), reader.GetInt64(1), reader.GetString(2));
    }

    /// <summary>The instance's revision, which the database numbers on every write.</summary>
    public static async Task<long> RevisionAsync(Guid id) =>
        (long)
            (
                await ScalarAsync(
                    "SELECT revision FROM trax.snapshot_draft WHERE machine = 'topic-map' AND id = @id",
                    id
                )
            )!;

    /// <summary>Whether the host still stores the instance.</summary>
    public static async Task<bool> ExistsAsync(Guid id) =>
        (bool)
            (
                await ScalarAsync(
                    "SELECT EXISTS (SELECT 1 FROM trax.snapshot_draft WHERE machine = 'topic-map' AND id = @id)",
                    id
                )
            )!;

    /// <summary>The row's <c>due_at</c>, or null when its state has no timer.</summary>
    public static async Task<DateTimeOffset?> DueAtAsync(Guid id) =>
        await ScalarAsync(
            "SELECT due_at FROM trax.snapshot_draft WHERE machine = 'topic-map' AND id = @id",
            id
        )
            is DateTime due
            ? new DateTimeOffset(DateTime.SpecifyKind(due, DateTimeKind.Utc))
            : null;

    /// <summary>Moves the instance's pending timer into the past, so the host's next sweep fires it.</summary>
    public static async Task MakeTheTimerDueAsync(Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE trax.snapshot_draft SET due_at = now() - interval '1 second'
            WHERE machine = 'topic-map' AND id = @id AND due_at IS NOT NULL
            """,
            connection
        );
        command.Parameters.AddWithValue("id", id);
        (await command.ExecuteNonQueryAsync())
            .Should()
            .Be(1, "the instance is in a state with a timer, so its due_at is set");
    }

    /// <summary>Whether the instance's state waits on a run it invoked (it holds an invoke token).</summary>
    public static async Task<bool> HasLiveRunAsync(Guid id) =>
        (bool)
            (
                await ScalarAsync(
                    "SELECT invoke_token IS NOT NULL FROM trax.snapshot_draft WHERE machine = 'topic-map' AND id = @id",
                    id
                )
            )!;

    /// <summary>The state of the latest run the instance's Building state queued, as Postgres spells it.</summary>
    public static async Task<string?> RunStateAsync(Guid id) =>
        (string?)
            await ScalarAsync(
                """
                SELECT train_state::text FROM trax.metadata
                WHERE invoking_instance_id = @id ORDER BY id DESC LIMIT 1
                """,
                id
            );

    /// <summary>The id of the latest run the instance invoked, or null.</summary>
    public static async Task<long?> LatestRunIdAsync(Guid id) =>
        (long?)
            await ScalarAsync(
                "SELECT id FROM trax.metadata WHERE invoking_instance_id = @id ORDER BY id DESC LIMIT 1",
                id
            );

    private static async Task<object?> ScalarAsync(string sql, Guid id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(RecoveryBrowserHost.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
