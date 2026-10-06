using AwesomeAssertions;
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
            group: new ManifestGroupSeed(PriorityIfNew: 1)
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
            group: new ManifestGroupSeed(PriorityIfNew: 1)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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

    #region Re-seeding an existing dependent or one-off manifest's enabled state

    [Test]
    public async Task UpsertDependentManifestAsync_Existing_WritesEnabledOnlyWhenTheOptionsStateIt()
    {
        var parent = await DataContext.UpsertManifestAsync(
            typeof(SchedulerTestTrain),
            $"en-parent-{Guid.NewGuid():N}",
            new SchedulerTestInput(),
            IntervalSchedule,
            new ManifestOptions(),
            groupId: "en-group",
            group: new ManifestGroupSeed(PriorityIfNew: 0)
        );
        await DataContext.SaveChanges(CancellationToken.None);
        var childId = $"en-child-{Guid.NewGuid():N}";

        Task<Trax.Effect.Models.Manifest.Manifest> Seed(ManifestOptions options) =>
            DataContext.UpsertDependentManifestAsync(
                typeof(SchedulerTestTrain),
                childId,
                new SchedulerTestInput(),
                parent.Id,
                options,
                groupId: "en-group",
                group: new ManifestGroupSeed(PriorityIfNew: 0)
            );

        await AssertEnabledIsWrittenOnlyWhenStated(childId, Seed);
    }

    [Test]
    public async Task UpsertOnceManifestAsync_Existing_WritesEnabledOnlyWhenTheOptionsStateIt()
    {
        var externalId = $"en-once-{Guid.NewGuid():N}";

        Task<Trax.Effect.Models.Manifest.Manifest> Seed(ManifestOptions options) =>
            DataContext.UpsertOnceManifestAsync(
                typeof(SchedulerTestTrain),
                externalId,
                new SchedulerTestInput(),
                DateTime.UtcNow.AddHours(1),
                options,
                groupId: "en-once-group",
                group: new ManifestGroupSeed(PriorityIfNew: 0)
            );

        await AssertEnabledIsWrittenOnlyWhenStated(externalId, Seed);
    }

    /// <summary>
    /// Seeds the manifest, disables it as an operator would, then re-seeds it without and with
    /// the enabled state stated.
    /// </summary>
    private async Task AssertEnabledIsWrittenOnlyWhenStated(
        string externalId,
        Func<ManifestOptions, Task<Trax.Effect.Models.Manifest.Manifest>> seed
    )
    {
        await seed(new ManifestOptions());
        await DataContext.SaveChanges(CancellationToken.None);
        await DataContext
            .Manifests.Where(m => m.ExternalId == externalId)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsEnabled, false));
        DataContext.Reset();

        var reseeded = await seed(new ManifestOptions());
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        reseeded
            .IsEnabled.Should()
            .BeFalse("options that do not state Enabled leave the operator's choice alone");

        var stated = await seed(new ManifestOptions { IsEnabled = true });
        await DataContext.SaveChanges(CancellationToken.None);
        stated.IsEnabled.Should().BeTrue("options that state Enabled write it");
    }

    #endregion

    #region A cron's first occurrence across re-seeds

    [Test]
    public async Task UpsertManifestAsync_ReseedingACronThatHasNotRun_KeepsOrRecomputesItsFirstOccurrence()
    {
        var externalId = $"cron-first-{Guid.NewGuid():N}";

        Task<Trax.Effect.Models.Manifest.Manifest> Seed(Schedule schedule) =>
            DataContext.UpsertManifestAsync(
                typeof(SchedulerTestTrain),
                externalId,
                new SchedulerTestInput(),
                schedule,
                new ManifestOptions(),
                groupId: "cron-first-group",
                group: new ManifestGroupSeed(PriorityIfNew: 0)
            );

        async Task<DateTime?> SeedAndRead(Schedule schedule)
        {
            await Seed(schedule);
            await DataContext.SaveChanges(CancellationToken.None);
            DataContext.Reset();
            return await DataContext
                .Manifests.AsNoTracking()
                .Where(m => m.ExternalId == externalId)
                .Select(m => m.NextScheduledRun)
                .SingleAsync();
        }

        var daily = Schedule.FromCron("0 3 * * *");
        (await SeedAndRead(daily)).Should().NotBeNull("a new cron waits for its first occurrence");

        // Stand in for an occurrence recorded earlier, so keeping it is distinguishable from
        // computing the next one again.
        var recorded = DateTime.UtcNow.AddDays(2).Date.AddHours(3);
        await DataContext
            .Manifests.Where(m => m.ExternalId == externalId)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.NextScheduledRun, recorded));

        (await SeedAndRead(daily))
            .Should()
            .Be(recorded, "re-stating the same cron keeps the pending first occurrence");

        var hourly = Schedule.FromCron("0 * * * *");
        var afterChange = await SeedAndRead(hourly);
        afterChange
            .Should()
            .NotBe(recorded, "a different cron computes its own first occurrence")
            .And.BeAfter(DateTime.UtcNow)
            .And.BeBefore(DateTime.UtcNow.AddHours(1).AddMinutes(1));

        (await SeedAndRead(IntervalSchedule))
            .Should()
            .BeNull("an interval schedule has no pre-computed occurrence");

        await DataContext
            .Manifests.Where(m => m.ExternalId == externalId)
            .ExecuteUpdateAsync(u =>
                u.SetProperty(m => m.ScheduleType, ScheduleType.Cron)
                    .SetProperty(m => m.CronExpression, "0 3 * * *")
                    .SetProperty(m => m.NextScheduledRun, (DateTime?)null)
            );
        (await SeedAndRead(daily))
            .Should()
            .NotBeNull("the same cron with no occurrence recorded computes one");
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
                group: new ManifestGroupSeed(PriorityIfNew: 0)
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
                group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            group: new ManifestGroupSeed(PriorityIfNew: 0)
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
            new ManifestGroupSeed(
                Priority: 1,
                MaxActiveJobsStated: true,
                MaxActiveJobs: 5,
                IsEnabled: true
            )
        );
        await DataContext.SaveChanges(CancellationToken.None);

        var secondId = await DataContext.EnsureManifestGroupAsync(
            name,
            new ManifestGroupSeed(
                Priority: 9,
                MaxActiveJobsStated: true,
                MaxActiveJobs: 10,
                IsEnabled: false
            )
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

    [Test]
    public async Task EnsureManifestGroupAsync_ExistingGroup_KeepsSettingsTheSeedDoesNotState()
    {
        var name = $"keep-{Guid.NewGuid():N}";

        var id = await DataContext.EnsureManifestGroupAsync(
            name,
            new ManifestGroupSeed(
                Priority: 4,
                MaxActiveJobsStated: true,
                MaxActiveJobs: 3,
                IsEnabled: false
            )
        );
        await DataContext.SaveChanges(CancellationToken.None);

        await DataContext.EnsureManifestGroupAsync(name, new ManifestGroupSeed(PriorityIfNew: 30));
        await DataContext.SaveChanges(CancellationToken.None);

        var reloaded = await DataContext.ManifestGroups.AsNoTracking().FirstAsync(g => g.Id == id);
        reloaded.Priority.Should().Be(4);
        reloaded.MaxActiveJobs.Should().Be(3);
        reloaded.IsEnabled.Should().BeFalse();
    }

    #endregion
}
