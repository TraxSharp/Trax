using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Recovery.Corpus;

namespace Trax.Samples.Recovery.Index;

/// <summary>
/// Loads <see cref="IndexFixture"/> into <c>topic_map.source_records</c> at startup. Each record is
/// inserted with <c>ON CONFLICT DO NOTHING</c>, so a record already there is left as it is, a second
/// start adds nothing, and two hosts starting together insert each record once.
/// </summary>
public static class IndexSeeder
{
    /// <summary>Creates the schema if it is missing and adds the records it lacks.</summary>
    /// <returns>How many records were added.</returns>
    public static async Task<int> SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        // EnsureSchemaCreatedAsync is for demos and tests; a real application uses migrations.
        await services
            .EnsureSchemaCreatedAsync<TopicMapDbContext>(cancellationToken)
            .ConfigureAwait(false);

        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var factory = scope.ServiceProvider.GetRequiredService<
                IDbContextFactory<TopicMapDbContext>
            >();
            var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using (db.ConfigureAwait(false))
            {
                await using var transaction = await db.Database.BeginTransactionAsync(
                    cancellationToken
                );

                var added = 0;
                foreach (var record in IndexFixture.Records)
                    added += await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        INSERT INTO topic_map.source_records ("Source", "SourceId", "Month", "Payload")
                        VALUES ({record.Source}, {record.SourceId}, {record.Month}, {record.Payload}::jsonb)
                        ON CONFLICT ("Source", "SourceId") DO NOTHING
                        """,
                        cancellationToken
                    );

                await transaction.CommitAsync(cancellationToken);
                return added;
            }
        }
    }
}
