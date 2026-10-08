using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// Creates the <c>topic_map</c> schema and loads <see cref="CorpusFixture"/> into it at startup.
/// A work already there is left as it is, so starting the host again adds nothing.
/// </summary>
public static class CorpusSeeder
{
    /// <summary>Creates the schema if it is missing and adds the works it lacks.</summary>
    /// <returns>How many works were added.</returns>
    public static async Task<int> SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        // EnsureSchemaCreatedAsync is for demos and tests; a real application uses migrations.
        await services.EnsureSchemaCreatedAsync<TopicMapDbContext>(cancellationToken);

        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<
            IDbContextFactory<TopicMapDbContext>
        >();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var ids = CorpusFixture.Works.Select(w => w.Id).ToList();
        var present = await db
            .Works.Where(w => ids.Contains(w.Id))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        var missing = CorpusFixture
            .Works.Where(w => !present.Contains(w.Id))
            .Select(w => new Work
            {
                Id = w.Id,
                Title = w.Title,
                Abstract = w.Abstract,
                Year = w.Year,
                Field = w.Field,
                Authors = [.. w.Authors],
                References = [.. w.References],
                Concepts = [.. w.Concepts],
            })
            .ToList();

        if (missing.Count == 0)
            return 0;

        db.Works.AddRange(missing);
        await db.SaveChangesAsync(cancellationToken);
        return missing.Count;
    }
}
