using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Extensions;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// Factory for per-request data contexts and stores over the throwaway database (<see cref="PostgresSetup"/>),
/// built the way a host builds them: <c>UsePostgres</c>, the data context it registers, and its dialect.
/// </summary>
public static class TestDb
{
    private static readonly Lazy<ServiceProvider> Provider = new(() =>
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(PostgresSetup.ConnectionString))
        );
        return services.BuildServiceProvider();
    });

    /// <summary>A fresh data context, the analogue of one request's.</summary>
    public static IDataContext NewContext() =>
        (IDataContext)Provider.Value.GetRequiredService<IDataContextProviderFactory>().Create();

    /// <summary>The Postgres dialect, which reads a unique violation as a lost race.</summary>
    public static ISqlDialect Dialect => Provider.Value.GetRequiredService<ISqlDialect>();

    public static EfSnapshotStore NewStore(IDataContext? context = null) =>
        new(context ?? NewContext(), Dialect);

    public static EfEffectClaimStore NewClaims(IDataContext? context = null) =>
        new(context ?? NewContext(), Dialect);

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
