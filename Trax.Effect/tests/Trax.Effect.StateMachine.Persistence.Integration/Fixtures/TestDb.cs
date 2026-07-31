using Microsoft.EntityFrameworkCore;

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
}
