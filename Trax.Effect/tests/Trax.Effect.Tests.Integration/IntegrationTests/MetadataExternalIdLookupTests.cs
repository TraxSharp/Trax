using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>metadata.external_id</c> is <c>char(32)</c> on Postgres. The model has to say so: mapped as
/// text, every LINQ lookup by external id sends a text parameter, Postgres casts the column to
/// text to compare it, and <c>ix_metadata_external_id</c> no longer applies, so the lookup reads
/// every run.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MetadataExternalIdLookupTests : TestSetup
{
    [Test]
    public async Task A_linq_lookup_by_external_id_reads_the_index()
    {
        using var context = (DbContext)DataContextFactory.Create();
        var externalId = Guid.NewGuid().ToString("N");

        var plan = await Explain(
            context,
            ((IDataContext)context).Metadatas.Where(m => m.ExternalId == externalId)
        );

        plan.Should()
            .Contain(
                "ix_metadata_external_id",
                $"the parameter has to compare as char(32) to use the index. The plan was:\n{plan}"
            );
    }

    [Test]
    public async Task A_lookup_finds_the_run_with_that_external_id_and_no_other()
    {
        var wanted = await Persist(Guid.NewGuid().ToString("N"));
        await Persist(Guid.NewGuid().ToString("N"));

        using var context = (IDataContext)DataContextFactory.Create();
        var found = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.ExternalId == wanted.ExternalId)
            .Select(m => new { m.Id, m.ExternalId })
            .ToListAsync();

        found.Should().ContainSingle().Which.Id.Should().Be(wanted.Id);
        found[0].ExternalId.Should().Be(wanted.ExternalId);
    }

    [Test]
    public async Task A_longer_value_that_starts_with_a_stored_id_does_not_match_it()
    {
        // A char(32) parameter must not be cut to 32 characters on the way to the server, or a
        // caller's longer key would find a run whose id is its prefix.
        var stored = await Persist(Guid.NewGuid().ToString("N"));
        var longer = stored.ExternalId + "extra";

        using var context = (IDataContext)DataContextFactory.Create();
        var found = await context.Metadatas.AsNoTracking().AnyAsync(m => m.ExternalId == longer);

        found.Should().BeFalse();
    }

    [Test]
    public async Task A_shorter_value_that_is_a_prefix_of_a_stored_id_does_not_match_it()
    {
        var stored = await Persist(Guid.NewGuid().ToString("N"));
        var prefix = stored.ExternalId[..8];

        using var context = (IDataContext)DataContextFactory.Create();
        var found = await context.Metadatas.AsNoTracking().AnyAsync(m => m.ExternalId == prefix);

        found.Should().BeFalse();
    }

    [Test]
    public async Task A_short_external_id_is_found_by_the_value_it_was_written_with()
    {
        // char(32) pads a shorter value with spaces. Comparison ignores the padding, so the value
        // the caller wrote still finds the run.
        var stored = await Persist("short-id");

        using var context = (IDataContext)DataContextFactory.Create();
        var found = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.ExternalId == "short-id")
            .Select(m => m.Id)
            .ToListAsync();

        found.Should().ContainSingle().Which.Should().Be(stored.Id);
    }

    private async Task<Metadata> Persist(string externalId)
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.Effect.Tests.ExternalIdLookup",
                ExternalId = externalId,
                Input = null,
            }
        );
        metadata.TrainState = TrainState.Completed;
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);
        return metadata;
    }

    private static async Task<string> Explain<T>(DbContext context, IQueryable<T> query)
    {
        await using var command = (NpgsqlCommand)query.CreateDbCommand();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        await using (
            var off = new NpgsqlCommand(
                "SET LOCAL enable_seqscan = off",
                command.Connection,
                command.Transaction
            )
        )
            await off.ExecuteNonQueryAsync();

        command.CommandText = "EXPLAIN " + command.CommandText;
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                lines.Add(reader.GetString(0));

        return string.Join('\n', lines);
    }
}
