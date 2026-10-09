using System.Text;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The subject key limit counts characters, and a character can take four bytes in UTF-8, so the
/// longest key <see cref="WorkQueue.Create"/> accepts has to fit the Postgres btree entry limit
/// (2704 bytes) in both partial subject indexes: <c>ix_work_queue_subject_queued</c> takes the key on
/// insert, and <c>ix_work_queue_subject_busy</c> takes it again once the entry is dispatched. Before
/// the queued index, a key that did not fit inserted fine and then failed every claim.
///
/// <para>Enforces Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md")]
public class SubjectKeyStorageTests : TestSetup
{
    [Test]
    [LeavesStuckRuns(
        "marks an entry dispatched by hand so the key has to fit the busy index, with no run behind it"
    )]
    public async Task A_key_of_the_most_four_byte_characters_allowed_is_indexed_when_dispatched()
    {
        var key = FourByteKey(WorkQueue.MaxSubjectKeyLength);
        Encoding.UTF8.GetByteCount(key).Should().Be(4 * WorkQueue.MaxSubjectKeyLength);

        using (var context = (IDataContext)DataContextFactory.Create())
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue { TrainName = "Subject.Key.Train", SubjectKey = key }
            );
            await context.Track(entry);
            await context.SaveChanges(CancellationToken.None);

            // ix_work_queue_subject_busy is partial on status = 'dispatched', so this update is
            // the write that has to fit that index; the insert above had to fit the queued one.
            entry.Status = WorkQueueStatus.Dispatched;
            var act = () => context.SaveChanges(CancellationToken.None);

            await act.Should()
                .NotThrowAsync(
                    "512 characters of four bytes each is 2048 bytes, inside the 2704-byte "
                        + "btree entry limit"
                );
        }

        using var readBack = (IDataContext)DataContextFactory.Create();
        var stored = await readBack.WorkQueues.AsNoTracking().SingleAsync(q => q.SubjectKey == key);

        stored.Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    /// <summary>
    /// Distinct supplementary-plane characters in a fixed, non-repeating order, so the value does
    /// not compress and every one of them costs four bytes in the index.
    /// </summary>
    private static string FourByteKey(int characters)
    {
        var builder = new StringBuilder(characters * 2);
        for (var i = 0; i < characters; i++)
            builder.Append(char.ConvertFromUtf32(0x10000 + (i * 7919 % 0xF0000)));

        return builder.ToString();
    }
}
