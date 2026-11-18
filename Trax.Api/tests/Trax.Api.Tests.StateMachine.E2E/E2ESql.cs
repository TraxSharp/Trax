using Npgsql;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Raw reads of an E2E database, for the assertions that must not go through the service under test: what a row
/// holds, and how many runs a request recorded.
/// </summary>
public static class E2ESql
{
    /// <summary>The canonical names of the internal trains every machine event runs through.</summary>
    public static readonly IReadOnlyList<string> MachineTrainNames =
    [
        typeof(IAdvanceSnapshot).FullName!,
        typeof(ISendSnapshot).FullName!,
        typeof(ISaveSnapshot).FullName!,
        typeof(ILoadSnapshot).FullName!,
    ];

    /// <summary>The first column of the first row, or default when there is none.</summary>
    public static async Task<T?> ScalarAsync<T>(
        string database,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        var connection = new NpgsqlConnection(E2EHost.ConnectionString(database));
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value);
                var value1 = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return value1 is null or DBNull ? default : (T)value1;
            }
        }
    }

    /// <summary>Runs a statement and returns the rows it affected.</summary>
    public static async Task<int> ExecuteAsync(
        string database,
        string sql,
        params (string Name, object Value)[] parameters
    )
    {
        var connection = new NpgsqlConnection(E2EHost.ConnectionString(database));
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                foreach (var (name, value) in parameters)
                    command.Parameters.AddWithValue(name, value);
                return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The draft row of instance <paramref name="id"/> of <paramref name="machine"/>, or null when there is none.
    /// The concurrency token changes on every write, so an unchanged row proves nothing was written.
    /// </summary>
    public static async Task<DraftRow?> DraftAsync(string database, string machine, Guid id)
    {
        var connection = new NpgsqlConnection(E2EHost.ConnectionString(database));
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new NpgsqlCommand(
                "SELECT state, context::text, concurrency_token, updated_at, user_key "
                    + "FROM trax.snapshot_draft WHERE machine = @machine AND id = @id",
                connection
            );
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("machine", machine);
                command.Parameters.AddWithValue("id", id);
                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync().ConfigureAwait(false))
                        return null;
                    return new DraftRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetGuid(2),
                        reader.GetFieldValue<DateTimeOffset>(3),
                        reader.GetString(4)
                    );
                }
            }
        }
    }

    /// <summary>How many draft rows <paramref name="machine"/> has.</summary>
    public static async Task<long> CountDraftsAsync(string database, string machine) =>
        await ScalarAsync<long>(
                database,
                "SELECT count(*) FROM trax.snapshot_draft WHERE machine = @machine",
                ("machine", machine)
            )
            .ConfigureAwait(false);

    /// <summary>How many runs of the internal machine trains are recorded.</summary>
    public static async Task<long> CountMachineRunsAsync(string database) =>
        await ScalarAsync<long>(
                database,
                "SELECT count(*) FROM trax.metadata WHERE name = ANY(@names)",
                ("names", MachineTrainNames.ToArray())
            )
            .ConfigureAwait(false);

    /// <summary>How many runs instance <paramref name="instance"/>'s invoking states queued.</summary>
    public static async Task<long> CountInvokedRunsAsync(string database, Guid instance) =>
        await ScalarAsync<long>(
                database,
                "SELECT count(*) FROM trax.work_queue WHERE invoking_instance_id = @id",
                ("id", instance)
            )
            .ConfigureAwait(false);

    /// <summary>How many exactly-once effect claims start with <paramref name="keyPrefix"/>.</summary>
    public static async Task<long> CountEffectClaimsAsync(string database, string keyPrefix) =>
        await ScalarAsync<long>(
                database,
                "SELECT count(*) FROM trax.effect_claim WHERE starts_with(effect_key, @prefix)",
                ("prefix", keyPrefix)
            )
            .ConfigureAwait(false);

    /// <summary>The recorded inputs of the internal machine trains' runs, oldest first.</summary>
    public static async Task<IReadOnlyList<string>> MachineRunInputsAsync(string database)
    {
        var connection = new NpgsqlConnection(E2EHost.ConnectionString(database));
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var command = new NpgsqlCommand(
                "SELECT coalesce(input::text, '') FROM trax.metadata WHERE name = ANY(@names) ORDER BY id",
                connection
            );
            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("names", MachineTrainNames.ToArray());
                var inputs = new List<string>();
                var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                        inputs.Add(reader.GetString(0));
                    return inputs;
                }
            }
        }
    }
}

/// <summary>One <c>snapshot_draft</c> row, as <see cref="E2ESql.DraftAsync"/> reads it.</summary>
public sealed record DraftRow(
    string State,
    string Context,
    Guid ConcurrencyToken,
    DateTimeOffset UpdatedAt,
    string UserKey
);
