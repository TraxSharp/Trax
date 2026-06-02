using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Catalog;
using Trax.Samples.Bookworm.Catalog.Models.Books;

namespace Trax.Samples.Bookworm.E2E.UnitTests;

/// <summary>
/// Verifies the cross-schema read mechanism (the "recursive exception" / model-graph-leak
/// prevention): when a context that does not own the catalog schema maps <see cref="Book"/> via
/// <see cref="Book.OnCrossSchemaModelCreating"/>, the book is pinned to the catalog schema and every
/// navigation is ignored, so EF Core never walks the catalog model graph into the foreign context.
/// No database is needed; building the Npgsql model offline is enough to inspect it.
/// </summary>
[TestFixture]
public class CrossSchemaModelTests
{
    /// <summary>A test-only context that reads <see cref="Book"/> from the catalog schema cross-schema.</summary>
    private sealed class CrossSchemaProbeContext(DbContextOptions<CrossSchemaProbeContext> options)
        : DomainDataContext<CrossSchemaProbeContext>(options)
    {
        protected override string Schema => "probe";

        protected override void ConfigureModel(ModelBuilder modelBuilder) =>
            Book.OnCrossSchemaModelCreating(modelBuilder, CatalogSchema.Name);
    }

    private static CrossSchemaProbeContext BuildOfflineModel() =>
        new(
            new DbContextOptionsBuilder<CrossSchemaProbeContext>()
                .UseNpgsql("Host=localhost;Database=offline_model_only")
                .Options
        );

    [Test]
    public void CrossSchemaRead_PinsBookToCatalogSchema()
    {
        using var context = BuildOfflineModel();

        var bookEntity = context.Model.FindEntityType(typeof(Book));

        bookEntity.Should().NotBeNull();
        bookEntity!.GetSchema().Should().Be(CatalogSchema.Name);
    }

    [Test]
    public void CrossSchemaRead_IgnoresAllNavigations_PreventingGraphLeak()
    {
        using var context = BuildOfflineModel();

        var bookEntity = context.Model.FindEntityType(typeof(Book))!;

        bookEntity
            .GetNavigations()
            .Should()
            .BeEmpty(
                "OnCrossSchemaModelCreating must ignore every navigation so EF does not pull the "
                    + "catalog model graph (Author, and anything it reaches) into a foreign context"
            );
    }
}
