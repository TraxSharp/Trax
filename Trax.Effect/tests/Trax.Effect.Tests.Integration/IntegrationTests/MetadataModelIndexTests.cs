using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The data context's model declares the indexes the migrations build on <c>metadata</c>, so a
/// schema created from the model (<c>EnsureCreated</c>, as tests and throwaway databases do) has
/// them too.
/// </summary>
[TestFixture]
public class MetadataModelIndexTests : TestSetup
{
    [Test]
    public void The_model_declares_the_external_id_index_migration_050_builds()
    {
        using var context = (DbContext)DataContextFactory.Create();
        var metadata = context.Model.FindEntityType(typeof(Metadata))!;

        var index = metadata
            .GetIndexes()
            .SingleOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual(["ExternalId"]));

        index.Should().NotBeNull("050 indexes metadata.external_id for lookups by external id");
        index!.GetDatabaseName().Should().Be("ix_metadata_external_id");
        index.IsUnique.Should().BeFalse("one external id can name several runs");
    }
}
