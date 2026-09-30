using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The Postgres provider replaces a NUL in text it writes, so an outcome carrying one can still be
/// recorded. A value that identifies a row is compared exactly, and must never be rewritten: a key
/// holding <c>"a\0"</c> would be stored as <c>"a�"</c> and then match a different key. Postgres
/// refuses such a value instead.
/// </summary>
[TestFixture]
public class NulCharacterKeyTests : TestSetup
{
    [Test]
    public async Task A_NUL_in_an_identifying_column_is_refused_rather_than_rewritten()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "Nul.Key.Train" });
        var externalId = "nul\0" + Guid.NewGuid().ToString("N")[..20];
        entry.ExternalId = externalId;
        await context.Track(entry);

        var save = () => context.SaveChanges(CancellationToken.None);

        (await save.Should().ThrowAsync<Exception>())
            .Which.GetBaseException()
            .Should()
            .BeOfType<PostgresException>()
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.CharacterNotInRepertoire);

        using var readBack = (IDataContext)DataContextFactory.Create();
        var rewritten = externalId.Replace('\0', '�');
        (await readBack.WorkQueues.AsNoTracking().AnyAsync(q => q.ExternalId == rewritten))
            .Should()
            .BeFalse("an identifying value is stored as given or not at all");
    }

    [Test]
    public void A_subject_key_holding_a_NUL_is_refused_when_the_entry_is_created()
    {
        var create = () =>
            WorkQueue.Create(
                new CreateWorkQueue { TrainName = "Nul.Key.Train", SubjectKey = "a\0" }
            );

        create.Should().Throw<ArgumentException>().WithMessage("*NUL*");
    }
}
