using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Api.Tests.Stress.Fakes.Trains;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Api.Tests.Stress.Utils;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Mediator.Extensions;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// The seeder's timestamps must not depend on the session time zone. The Trax time columns are
/// <c>timestamptz</c>, so the newest seeded row of every table should sit at <c>now()</c> whatever
/// zone the seeding session runs in.
/// </summary>
/// <remarks>
/// Seeds a tiny profile into its own database over a session pinned to a zone far from UTC, so it
/// neither needs nor disturbs the millions of rows the other stress fixtures share.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class SeederTimeZoneTests
{
    private static readonly string ConnectionString =
        $"Host=localhost;Port={TestPostgres.Port};Database=trax_api_stress_tz;Username=trax;"
        + "Password=trax123;Timezone=Pacific/Kiritimati;Include Error Detail=true";

    [Test]
    public async Task SeedAsync_InANonUtcSession_StampsTheNewestRowsAtNow()
    {
        await using (var admin = new NpgsqlConnection(Maintenance()))
        {
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = "DROP DATABASE IF EXISTS trax_api_stress_tz WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
        BulkSeeder.EnsureDatabaseExists(ConnectionString);

        // Building the container runs the Trax migrations, which the seeder writes into.
        var services = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(ConnectionString).AddJson())
                    .AddMediator(typeof(StressProbeTrain).Assembly)
            );
        await using (services.BuildServiceProvider()) { }

        await BulkSeeder.SeedAsync(
            ConnectionString,
            new StressProfile(
                Metadata: 100,
                Log: 100,
                WorkQueue: 100,
                DeadLetter: 100,
                Manifests: 10,
                Groups: 2,
                TrainNames: 3,
                PersistedOperations: 10
            ),
            _ => { }
        );

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (
            var (table, column) in new[]
            {
                ("metadata", "start_time"),
                ("dead_letter", "dead_lettered_at"),
                ("work_queue", "created_at"),
            }
        )
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT extract(epoch FROM now() - max({column}))::float8 FROM trax.{table}";
            var lagSeconds = (double)(await command.ExecuteScalarAsync())!;

            lagSeconds
                .Should()
                .BeInRange(
                    0,
                    120,
                    $"the newest trax.{table}.{column} should be seeded at now(), not shifted by "
                        + "the session's offset from UTC"
                );
        }
    }

    private static string Maintenance() =>
        new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = "postgres",
        }.ConnectionString;
}
