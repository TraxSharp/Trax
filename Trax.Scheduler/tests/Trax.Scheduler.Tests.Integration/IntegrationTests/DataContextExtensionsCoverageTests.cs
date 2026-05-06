using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Direct coverage for the existing-row update branches and validation paths in
/// <see cref="DataContextExtensions"/>. The happy create paths are exercised by the
/// scheduler E2E tests; this fixture targets the second-call upsert paths plus the
/// variance / misfire validation that the scheduler API funnels through.
/// </summary>
[TestFixture]
public class DataContextExtensionsCoverageTests : TestSetup
{
    private static readonly Schedule IntervalSchedule = Schedule.FromInterval(
        TimeSpan.FromMinutes(5)
    );

    #region UpsertManifestAsync — existing manifest update branch

    [Test]
    public async Task UpsertManifestAsync_ExistingManifest_UpdatesAllFields()
    {
        var externalId = $"upsert-existing-{Guid.NewGuid():N}";

        await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput { Value = "first" },
            IntervalSchedule,
            new ManifestOptions { Priority = 1, MaxRetries = 1 },
            groupId: "g1",
            groupPriority: 1
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var updated = await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput { Value = "second" },
            Schedule.FromCron("0 0 * * *"),
            new ManifestOptions
            {
                Priority = 9,
                MaxRetries = 7,
                Timeout = TimeSpan.FromMinutes(2),
                MisfirePolicy = MisfirePolicy.FireOnceNow,
                MisfireThreshold = TimeSpan.FromMinutes(3),
            },
            groupId: "g1",
            groupPriority: 1
        );
        await DataContext.SaveChanges(CancellationToken.None);

        updated.ScheduleType.Should().Be(ScheduleType.Cron);
        updated.CronExpression.Should().Be("0 0 * * *");
        updated.Priority.Should().Be(9);
        updated.MaxRetries.Should().Be(7);
        updated.TimeoutSeconds.Should().Be(120);
        updated.MisfirePolicy.Should().Be(MisfirePolicy.FireOnceNow);
        updated.MisfireThresholdSeconds.Should().Be(180);
    }

    #endregion

    #region UpsertDependentManifestAsync — both branches

    [Test]
    public async Task UpsertDependentManifestAsync_NewThenExisting_UpdatesScheduleAndDependency()
    {
        var parentId = $"dep-parent-{Guid.NewGuid():N}";
        var parent = await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            parentId,
            new SchedulerTestInput(),
            IntervalSchedule,
            new ManifestOptions(),
            groupId: "dep-group",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);

        var childId = $"dep-child-{Guid.NewGuid():N}";

        // First call: create branch
        var created = await DataContext.UpsertDependentManifestAsync(
            typeof(SchedulerTestTrain),
            childId,
            new SchedulerTestInput { Value = "v1" },
            parent.Id,
            new ManifestOptions { IsDormant = false, Priority = 2 },
            groupId: "dep-group",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Second call: existing-branch update with IsDormant flipped on
        var updated = await DataContext.UpsertDependentManifestAsync(
            typeof(SchedulerTestTrain),
            childId,
            new SchedulerTestInput { Value = "v2" },
            parent.Id,
            new ManifestOptions
            {
                IsDormant = true,
                Priority = 5,
                MaxRetries = 4,
                Timeout = TimeSpan.FromSeconds(30),
                MisfirePolicy = MisfirePolicy.DoNothing,
            },
            groupId: "dep-group",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);

        created.ScheduleType.Should().Be(ScheduleType.Dependent);
        updated.Id.Should().Be(created.Id);
        updated.ScheduleType.Should().Be(ScheduleType.DormantDependent);
        updated.DependsOnManifestId.Should().Be(parent.Id);
        updated.Priority.Should().Be(5);
        updated.MaxRetries.Should().Be(4);
        updated.TimeoutSeconds.Should().Be(30);
        updated.CronExpression.Should().BeNull();
        updated.IntervalSeconds.Should().BeNull();
        updated.MisfirePolicy.Should().Be(MisfirePolicy.DoNothing);
    }

    #endregion

    #region UpsertOnceManifestAsync — both branches

    [Test]
    public async Task UpsertOnceManifestAsync_NewThenExisting_UpdatesScheduledAt()
    {
        var externalId = $"once-{Guid.NewGuid():N}";
        var firstAt = DateTime.UtcNow.AddHours(1);

        var created = await DataContext.UpsertOnceManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput { Value = "first" },
            firstAt,
            new ManifestOptions { Priority = 1 },
            groupId: "once-group",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var secondAt = DateTime.UtcNow.AddHours(3);
        var updated = await DataContext.UpsertOnceManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput { Value = "second" },
            secondAt,
            new ManifestOptions
            {
                Priority = 8,
                MaxRetries = 2,
                Timeout = TimeSpan.FromMinutes(10),
                MisfirePolicy = MisfirePolicy.FireOnceNow,
                MisfireThreshold = TimeSpan.FromMinutes(1),
            },
            groupId: "once-group",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);

        created.ScheduleType.Should().Be(ScheduleType.Once);
        updated.Id.Should().Be(created.Id);
        updated.ScheduleType.Should().Be(ScheduleType.Once);
        updated.ScheduledAt.Should().BeCloseTo(secondAt, TimeSpan.FromSeconds(1));
        updated.Priority.Should().Be(8);
        updated.MaxRetries.Should().Be(2);
        updated.TimeoutSeconds.Should().Be(600);
        updated.CronExpression.Should().BeNull();
        updated.IntervalSeconds.Should().BeNull();
        updated.MisfirePolicy.Should().Be(MisfirePolicy.FireOnceNow);
        updated.MisfireThresholdSeconds.Should().Be(60);
    }

    #endregion

    #region Variance validation

    [Test]
    public async Task UpsertManifestAsync_WithNegativeVariance_Throws()
    {
        var schedule = IntervalSchedule with { Variance = TimeSpan.FromSeconds(-5) };

        var act = async () =>
        {
            await DataContext.UpsertManifestAsync(
                typeof(SchedulerTestTrain),
                $"neg-var-{Guid.NewGuid():N}",
                new SchedulerTestInput(),
                schedule,
                new ManifestOptions(),
                groupId: "var-g",
                groupPriority: 0
            );
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*non-negative*");
    }

    [Test]
    public async Task UpsertManifestAsync_WithVarianceOnUnsupportedScheduleType_Throws()
    {
        var act = async () =>
        {
            await DataContext.UpsertManifestAsync(
                typeof(SchedulerTestTrain),
                $"bad-var-{Guid.NewGuid():N}",
                new SchedulerTestInput(),
                Schedule.FromInterval(TimeSpan.FromMinutes(1)) with
                {
                    Type = ScheduleType.OnDemand,
                    Variance = TimeSpan.FromSeconds(10),
                },
                new ManifestOptions(),
                groupId: "var-g2",
                groupPriority: 0
            );
        };

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Interval and Cron*");
    }

    [Test]
    public async Task UpsertManifestAsync_WithValidVariance_PersistsVarianceSeconds()
    {
        var externalId = $"var-{Guid.NewGuid():N}";
        var schedule = IntervalSchedule with { Variance = TimeSpan.FromSeconds(45) };

        var manifest = await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput(),
            schedule,
            new ManifestOptions(),
            groupId: "var-g3",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);

        manifest.VarianceSeconds.Should().Be(45);
    }

    [Test]
    public async Task UpsertManifestAsync_WithoutVariance_ClearsVarianceSeconds()
    {
        // First write with variance set, then upsert without it to hit the null-clearing branch.
        var externalId = $"clear-var-{Guid.NewGuid():N}";

        await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput(),
            IntervalSchedule with
            {
                Variance = TimeSpan.FromSeconds(20),
            },
            new ManifestOptions(),
            groupId: "var-g4",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var updated = await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            externalId,
            new SchedulerTestInput(),
            IntervalSchedule,
            new ManifestOptions(),
            groupId: "var-g4",
            groupPriority: 0
        );
        await DataContext.SaveChanges(CancellationToken.None);

        updated.VarianceSeconds.Should().BeNull();
    }

    #endregion

    #region EnsureManifestGroupAsync — existing-row branch

    [Test]
    public async Task EnsureManifestGroupAsync_ExistingGroup_UpdatesProperties()
    {
        var name = $"reuse-{Guid.NewGuid():N}";

        var firstId = await DataContext.EnsureManifestGroupAsync(
            name,
            priority: 1,
            maxActiveJobs: 5,
            isEnabled: true
        );
        await DataContext.SaveChanges(CancellationToken.None);

        var secondId = await DataContext.EnsureManifestGroupAsync(
            name,
            priority: 9,
            maxActiveJobs: 10,
            isEnabled: false
        );
        await DataContext.SaveChanges(CancellationToken.None);

        secondId.Should().Be(firstId);
        var reloaded = await DataContext
            .ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Id == firstId);
        reloaded.Priority.Should().Be(9);
        reloaded.MaxActiveJobs.Should().Be(10);
        reloaded.IsEnabled.Should().BeFalse();
    }

    #endregion
}
