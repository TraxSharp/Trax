using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A timestamp Trax writes is the same instant whatever time zone the writing session is in.
/// </summary>
/// <remarks>
/// EF sends a UTC <see cref="DateTime"/> as <c>timestamptz</c>. Into a <c>timestamp without time
/// zone</c> column Postgres converts it with the session's <c>TimeZone</c>, so a session in New York
/// stored the wall-clock time four or five hours back, and it read back as that many hours early.
/// Every column here is <c>timestamptz</c>, so the instant survives.
/// <para>Enforces <c>docs/adr/0015-every-trax-timestamp-is-timestamptz.md</c>.</para>
/// </remarks>
[TestFixture]
[Property("adr", "docs/adr/0015-every-trax-timestamp-is-timestamptz.md")]
public class SessionTimeZoneTests : TestSetup
{
    private const string Adr = "docs/adr/0015-every-trax-timestamp-is-timestamptz.md";

    private static readonly DateTime Created = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dispatched = new(2026, 1, 15, 12, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime NextRun = new(2026, 7, 1, 9, 15, 0, DateTimeKind.Utc);

    private interface ITimeZoneTrain;

    [Test]
    public async Task No_trax_column_is_a_timestamp_without_time_zone()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var columns = await ((DbContext)context)
            .Database.SqlQueryRaw<string>(
                """
                SELECT table_name || '.' || column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'trax' AND data_type = 'timestamp without time zone'
                  -- DbUp's own journal, which Trax neither writes nor reads.
                  AND table_name <> 'migrations'
                """
            )
            .ToListAsync();

        columns
            .Should()
            .BeEmpty(
                "a plain timestamp stores the writing session's wall-clock time, so the same "
                    + $"instant reads back differently per host (see {Adr})"
            );
    }

    [TestCase("America/New_York")]
    [TestCase("Europe/Berlin")]
    public async Task Timestamps_written_from_a_non_utc_session_read_back_unchanged(string zone)
    {
        long groupId,
            manifestId,
            entryId;

        using (var context = (IDataContext)DataContextFactory.Create())
        {
            var db = (DbContext)context;
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlAsync($"SELECT set_config('TimeZone', {zone}, false)");

            var group = new ManifestGroup
            {
                Name = $"tz-{Guid.NewGuid():N}",
                CreatedAt = Created,
                UpdatedAt = Dispatched,
            };
            context.ManifestGroups.Add(group);
            await context.SaveChanges(CancellationToken.None);

            var manifest = Manifest.Create(new CreateManifest { Name = typeof(ITimeZoneTrain) });
            manifest.ManifestGroupId = group.Id;
            manifest.NextScheduledRun = NextRun;
            context.Manifests.Add(manifest);
            await context.SaveChanges(CancellationToken.None);

            var entry = WorkQueue.Create(
                new CreateWorkQueue { TrainName = typeof(ITimeZoneTrain).FullName! }
            );
            entry.CreatedAt = Created;
            entry.DispatchedAt = Dispatched;
            await context.Track(entry);
            await context.SaveChanges(CancellationToken.None);

            groupId = group.Id;
            manifestId = manifest.Id;
            entryId = entry.Id;
        }

        using var readBack = (IDataContext)DataContextFactory.Create();

        var storedGroup = await readBack
            .ManifestGroups.AsNoTracking()
            .SingleAsync(g => g.Id == groupId);
        var storedManifest = await readBack
            .Manifests.AsNoTracking()
            .SingleAsync(m => m.Id == manifestId);
        var storedEntry = await readBack
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == entryId);

        var because = $"a timestamp is the same instant whatever the session zone (see {Adr})";
        storedGroup.CreatedAt.Should().Be(Created, because);
        storedGroup.UpdatedAt.Should().Be(Dispatched, because);
        storedManifest.NextScheduledRun.Should().Be(NextRun, because);
        storedEntry.CreatedAt.Should().Be(Created, because);
        storedEntry.DispatchedAt.Should().Be(Dispatched, because);
    }

    [Test]
    public async Task A_defaulted_created_at_is_now_whatever_the_session_zone()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        var db = (DbContext)context;
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/New_York'");

        var externalId = Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO trax.work_queue (external_id, train_name) VALUES ({0}, 'Some.Train')",
            externalId
        );

        var skewSeconds = await db
            .Database.SqlQueryRaw<double>(
                """
                SELECT abs(extract(epoch FROM (now() - created_at)))::float8 AS "Value"
                FROM trax.work_queue WHERE external_id = {0}
                """,
                externalId
            )
            .SingleAsync();

        skewSeconds
            .Should()
            .BeLessThan(
                60,
                $"the column default is the current instant, not a wall-clock time (see {Adr})"
            );
    }
}
