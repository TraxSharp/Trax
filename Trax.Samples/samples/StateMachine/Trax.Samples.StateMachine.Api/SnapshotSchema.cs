using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.StateMachine.Api;

/// <summary>
/// Creates the sample's database (if missing) and the <c>snapshot_draft</c> + <c>effect_claim</c> tables.
/// A production host ships these as a migration; the sample does it on startup so nothing but
/// <c>docker compose up -d</c> is needed. This must run before <c>AddTrax</c>, because
/// <c>UsePostgres</c> migrates the Trax framework tables during service registration, and it uses a
/// dedicated database so the snapshot tables are created reliably no matter what else is in the cluster.
/// </summary>
internal static class SnapshotSchema
{
    public static async Task EnsureAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var database = target.Database!;

        // Connect to the always-present `postgres` database to create ours if it does not exist yet.
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        };
        await using (var admin = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{database}\"";
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "42P04")
            {
                // Database already exists — idempotent no-op.
            }
        }

        // On a fresh database this creates the `trax` schema + the two snapshot tables; on later runs the
        // tables already exist and this is a no-op. UsePostgres's DbUp then adds the framework tables
        // (its `create schema if not exists trax` coexists with the schema created here).
        await using var db = new SnapshotDbContext(
            new DbContextOptionsBuilder<SnapshotDbContext>().UseNpgsql(connectionString).Options
        );
        await db.Database.EnsureCreatedAsync();
    }
}
