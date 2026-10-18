using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.HostHeartbeats;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Host;
using Trax.Effect.Tests.Integration.Fixtures;
using HostRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The heartbeat row each host writes to <c>trax.host_heartbeat</c>, against a database built by the
/// shipped migrations. The writer is driven beat by beat on a clock the test moves, so nothing
/// waits on the 15-second interval.
/// </summary>
[TestFixture]
[NonParallelizable]
public class HostHeartbeatIntegrationTests : TestSetup
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly TraxHostInfo HostA = new()
    {
        HostName = "host-a",
        HostEnvironment = "kubernetes",
        HostInstanceId = "pod-a",
    };

    private static readonly TraxHostInfo HostB = new()
    {
        HostName = "host-b",
        HostEnvironment = "server",
        HostInstanceId = "host-b-4242",
    };

    // Runs after the base class's set-up, which builds DataContextFactory.
    [SetUp]
    public async Task ClearHostRows()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        await context.HostHeartbeats.ExecuteDeleteAsync();
    }

    [TearDown]
    public void ResetHostInfo() => TraxHostInfo.Current = null;

    private HostHeartbeatWriter Writer(ManualClock clock, params IHostHeartbeatRole[] roles) =>
        new(DataContextFactory, clock, roles);

    private async Task<List<HostRow>> Rows()
    {
        using var context = (IDataContext)DataContextFactory.Create();
        return await context.HostHeartbeats.AsNoTracking().OrderBy(h => h.InstanceId).ToListAsync();
    }

    [Test]
    public async Task Beat_KeepsOneRowPerHost_AndMovesItsLastBeatEveryBeat()
    {
        var clock = new ManualClock(Start);
        var a = Writer(clock);
        var b = Writer(clock);

        TraxHostInfo.Current = HostA;
        await a.Beat(CancellationToken.None);
        clock.Advance(HostRow.Interval);
        await a.Beat(CancellationToken.None);
        clock.Advance(HostRow.Interval);
        await a.Beat(CancellationToken.None);

        TraxHostInfo.Current = HostB;
        await b.Beat(CancellationToken.None);

        var rows = await Rows();
        rows.Select(r => r.InstanceId)
            .Should()
            .Equal(
                new[] { "host-b-4242", "pod-a" },
                "each process writes one row, keyed by its instance id"
            );

        var rowA = rows.Single(r => r.InstanceId == "pod-a");
        rowA.HostName.Should().Be("host-a");
        rowA.HostEnvironment.Should().Be("kubernetes");
        rowA.TraxVersion.Should().NotBeNullOrWhiteSpace();
        rowA.StartedAt.Should()
            .Be(Start.UtcDateTime, "the row keeps when the process began beating");
        rowA.LastHeartbeatAt.Should()
            .Be(
                (Start + HostRow.Interval * 2).UtcDateTime,
                "every beat rewrites the same row with the time of that beat"
            );
        rowA.RunsDispatcher.Should().BeFalse("nothing in this process runs the dispatcher");
        rowA.WorkerSlots.Should().BeNull("this process runs no local workers");
    }

    [Test]
    public async Task Beat_DeletesRowsWhoseLastBeatIsADayOld_AndKeepsTheRest()
    {
        await using (var context = (IDataContext)DataContextFactory.Create())
        {
            context.HostHeartbeats.Add(
                Row("gone", Start - HostRow.Retention - TimeSpan.FromSeconds(1))
            );
            context.HostHeartbeats.Add(
                Row("kept", Start - HostRow.Retention + TimeSpan.FromMinutes(1))
            );
            await context.SaveChanges(CancellationToken.None);
        }

        TraxHostInfo.Current = HostA;
        await Writer(new ManualClock(Start)).Beat(CancellationToken.None);

        (await Rows())
            .Select(r => r.InstanceId)
            .Should()
            .Equal(
                new[] { "kept", "pod-a" },
                "a row whose last beat is a day old is deleted by any host's beat"
            );
    }

    [Test]
    public async Task Beat_ReportsWhatTheRolesInThisProcessSay()
    {
        TraxHostInfo.Current = HostA;
        await Writer(new ManualClock(Start), new SchedulerAndWorkers())
            .Beat(CancellationToken.None);

        var row = (await Rows()).Single();
        row.RunsDispatcher.Should().BeTrue();
        row.RunsManifestManager.Should().BeTrue();
        row.WorkerSlots.Should().Be(4);
        row.WorkerSlotsInUse.Should().Be(3);
    }

    [Test]
    public void IsOnline_UntilThreeBeatsAreMissed()
    {
        var lastBeat = Start.UtcDateTime;

        HostRow.IsOnline(lastBeat, Start + HostRow.Interval).Should().BeTrue("one missed beat");
        HostRow
            .IsOnline(lastBeat, Start + HostRow.OfflineAfter - TimeSpan.FromSeconds(1))
            .Should()
            .BeTrue("fewer than three beats missed");
        HostRow
            .IsOnline(lastBeat, Start + HostRow.OfflineAfter)
            .Should()
            .BeFalse("three missed beats, 45 seconds, is offline");
        HostRow.OfflineAfter.Should().Be(TimeSpan.FromSeconds(45));
        HostRow
            .IsOnline(DateTime.SpecifyKind(lastBeat, DateTimeKind.Unspecified), Start)
            .Should()
            .BeTrue(
                "a time read back without a kind (SQLite) is taken as the UTC it was written in"
            );
    }

    [Test]
    public void UsePostgres_RegistersTheHeartbeatOnce()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                effects.SkipMigrations().UsePostgres("Host=localhost;Database=unused")
            )
        );

        services
            .Where(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(HostHeartbeatService)
            )
            .Should()
            .ContainSingle("every host with a data provider beats, once");
    }

    private static HostRow Row(string instanceId, DateTimeOffset lastBeat) =>
        new()
        {
            InstanceId = instanceId,
            HostName = instanceId,
            HostEnvironment = "server",
            TraxVersion = "0.0.0",
            StartedAt = lastBeat.UtcDateTime,
            LastHeartbeatAt = lastBeat.UtcDateTime,
        };

    private sealed class SchedulerAndWorkers : IHostHeartbeatRole
    {
        public void Describe(HostRow row)
        {
            row.RunsDispatcher = true;
            row.RunsManifestManager = true;
            row.WorkerSlots = 4;
            row.WorkerSlotsInUse = 3;
        }
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
