using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// SQLite stores <see cref="FailureClass"/> as its integer, and migration 009 defaults the
/// column to <c>0</c> for Unclassified, so the integers are part of the stored format. A
/// reordered or renumbered member would silently change what every existing row means.
///
/// <para>Enforces <c>docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-closed-vocabulary-is-a-postgres-enum.md")]
public class SqliteFailureClassStorageTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    [Test]
    public void Every_FailureClass_member_keeps_its_stored_integer()
    {
        Enum.GetValues<FailureClass>()
            .ToDictionary(value => value.ToString(), value => (int)value)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, int>
                {
                    ["Unclassified"] = 0,
                    ["Transient"] = 1,
                    ["Conflict"] = 2,
                    ["Permanent"] = 3,
                },
                "0006-a-closed-vocabulary-is-a-postgres-enum.md pins these because SQLite rows "
                    + "hold the integers; changing one reinterprets rows already written"
            );
    }

    [Test]
    public async Task A_failure_class_is_written_as_its_integer()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Sqlite.FailureClass",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        var failure = new InvalidOperationException("conflict");
        failure.Data["TrainExceptionData"] = new TrainExceptionData
        {
            FailureClass = FailureClass.Conflict,
            TrainName = "Sqlite.FailureClass",
            TrainExternalId = metadata.ExternalId,
            Type = nameof(InvalidOperationException),
            Junction = "Sqlite.Junction",
            Message = "conflict",
        };
        metadata.TrainState = TrainState.Failed;
        metadata.AddException(failure);
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var stored = await ((DbContext)context)
            .Database.SqlQueryRaw<long>(
                "SELECT failure_class AS \"Value\" FROM metadata WHERE id = {0}",
                metadata.Id
            )
            .SingleAsync();

        stored
            .Should()
            .Be(
                2,
                "0006-a-closed-vocabulary-is-a-postgres-enum.md: SQLite stores "
                    + "FailureClass.Conflict as the integer 2"
            );
    }
}
