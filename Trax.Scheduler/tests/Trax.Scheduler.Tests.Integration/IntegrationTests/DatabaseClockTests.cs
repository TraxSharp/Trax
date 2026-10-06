using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The time a dependent's dispatch and its parent's success are stamped with comes from the
/// database, not the process, so two machines with skewed clocks still agree on their order.
///
/// <para>Enforces <c>docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
)]
public class DatabaseClockTests : TestSetup
{
    [Test]
    public void The_time_is_read_from_the_database_server()
    {
        var sql = DataContext
            .Manifests.Select(_ => (DateTime?)DateTime.UtcNow)
            .Take(1)
            .ToQueryString();

        sql.Should()
            .Contain(
                "now()",
                "the projection must run on the server (PostgreSQL's now()). See docs/adr/0014-a-manifests-retries-count-recent-failures-and-a-cancelled-run-consumes-its-occurrence.md"
            );
    }

    [Test]
    public async Task The_time_is_utc_and_current()
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"clock-{Guid.NewGuid():N}"
        );

        var now = await DatabaseClock.UtcNowAsync(
            DataContext.ManifestGroups.Where(g => g.Id == group.Id),
            CancellationToken.None
        );

        now.Kind.Should().Be(DateTimeKind.Utc);
        now.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }
}
