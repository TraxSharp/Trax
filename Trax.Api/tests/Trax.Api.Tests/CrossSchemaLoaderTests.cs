using FluentAssertions;
using GreenDonut;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;

namespace Trax.Api.Tests;

/// <summary>
/// Verifies <see cref="CrossSchemaLoader{TContext, TEntity}"/> batches a set of keys into a single
/// query against the owning context and keys the results by Id, including returning null for an
/// absent key. Runs against SQLite so the <c>WHERE Id IN (...)</c> translation is exercised for real.
/// </summary>
[TestFixture]
public class CrossSchemaLoaderTests
{
    private sealed class Widget
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();
    }

    // Hands the loader a fresh context bound to the same (open) connection for each batch.
    private sealed class SharedConnectionFactory(DbContextOptions<WidgetContext> options)
        : IDbContextFactory<WidgetContext>
    {
        public WidgetContext CreateDbContext() => new(options);
    }

    [Test]
    public async Task LoadAsync_batches_keys_and_keys_results_by_id()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<WidgetContext>().UseSqlite(connection).Options;

        await using (var seed = new WidgetContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Widgets.AddRange(
                new Widget { Id = 1, Name = "one" },
                new Widget { Id = 2, Name = "two" },
                new Widget { Id = 3, Name = "three" }
            );
            await seed.SaveChangesAsync();
        }

        var loader = new CrossSchemaLoader<WidgetContext, Widget>(
            new SharedConnectionFactory(options),
            AutoBatchScheduler.Default,
            new DataLoaderOptions()
        );

        // Loading several keys at once collapses into one batch; an absent key resolves to null.
        var results = await Task.WhenAll(
            loader.LoadAsync(1),
            loader.LoadAsync(3),
            loader.LoadAsync(99)
        );

        results[0]!.Name.Should().Be("one");
        results[1]!.Name.Should().Be("three");
        results[2].Should().BeNull("key 99 does not exist in the owning context");
    }
}
