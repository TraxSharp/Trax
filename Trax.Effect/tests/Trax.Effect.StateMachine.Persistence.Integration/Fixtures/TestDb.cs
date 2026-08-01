using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>Factory for per-request contexts and stores over the throwaway database (<see cref="PostgresSetup"/>).</summary>
public static class TestDb
{
    public static SnapshotDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SnapshotDbContext>()
                .UseNpgsql(PostgresSetup.ConnectionString)
                .Options
        );

    public static EfSnapshotStore NewStore() => new(NewContext());

    public static EfEffectClaimStore NewClaims() => new(NewContext());

    /// <summary>
    /// Force a draft's <c>updated_at</c> to a fixed instant (the store always stamps "now"). This is how the
    /// TTL/expiry tests make a draft look abandoned deterministically, the analogue of the effect-claim
    /// tests' negative lease. Pass an instant comfortably past the TTL under test.
    /// </summary>
    public static async Task BackdateDraft(string userKey, Guid id, DateTimeOffset updatedAt)
    {
        await using var conn = new NpgsqlConnection(PostgresSetup.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "UPDATE trax.snapshot_draft SET updated_at = @ts WHERE user_key = @uk AND id = @id";
        cmd.Parameters.AddWithValue("@ts", updatedAt);
        cmd.Parameters.AddWithValue("@uk", userKey);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
