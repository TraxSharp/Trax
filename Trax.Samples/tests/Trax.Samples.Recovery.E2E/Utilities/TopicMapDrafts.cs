using Npgsql;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// Clears a test database of the <c>topic-map</c> drafts earlier runs left. The test databases outlive a run, every
/// test starts its drafts as the same demo user, and the host caps the drafts one user may hold, so a suite starts
/// from none rather than inheriting every earlier run's and being refused with <c>too-many-drafts</c>.
/// </summary>
public static class TopicMapDrafts
{
    /// <summary>Deletes every <c>topic-map</c> draft in the database <paramref name="connectionString"/> names.</summary>
    public static async Task ClearAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM trax.snapshot_draft WHERE machine = @machine",
            connection
        );
        command.Parameters.AddWithValue("machine", TopicMapMachine.MachineId);
        await command.ExecuteNonQueryAsync();
    }
}
