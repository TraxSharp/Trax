using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Coverage for the TraxScheduler internal Untyped overloads (used by InferredScheduling),
/// the empty-source short-circuits, exception/rollback paths, group operations, and
/// PruneSafeAsync's catch-block.
/// </summary>
[TestFixture]
public class TraxSchedulerUntypedAndEdgeTests : TestSetup
{
    private TraxScheduler _scheduler = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _scheduler = (TraxScheduler)Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
    }

    private static readonly Schedule Interval = Schedule.FromInterval(TimeSpan.FromMinutes(5));

    #region Untyped overloads

    [Test]
    public async Task ScheduleAsyncUntyped_PersistsManifestWithInterfaceFullName()
    {
        var externalId = $"untyped-{Guid.NewGuid():N}";

        var manifest = await _scheduler.ScheduleAsyncUntyped(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            externalId,
            new SchedulerTestInput { Value = "x" },
            Interval
        );

        manifest.ExternalId.Should().Be(externalId);
        manifest.Name.Should().Be(typeof(SchedulerTestTrain).FullName);
        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
    }

    [Test]
    public async Task ScheduleOnceAsyncUntyped_PersistsManifestWithScheduledAt()
    {
        var externalId = $"untyped-once-{Guid.NewGuid():N}";

        var manifest = await _scheduler.ScheduleOnceAsyncUntyped(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            externalId,
            new SchedulerTestInput(),
            TimeSpan.FromMinutes(30)
        );

        manifest.ScheduleType.Should().Be(ScheduleType.Once);
        manifest.ScheduledAt.Should().NotBeNull();
        manifest.ScheduledAt!.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(25));
    }

    [Test]
    public async Task ScheduleDependentAsyncUntyped_NewParent_PersistsDependent()
    {
        var parentId = $"u-parent-{Guid.NewGuid():N}";
        await _scheduler.ScheduleAsyncUntyped(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            parentId,
            new SchedulerTestInput(),
            Interval
        );

        var childId = $"u-child-{Guid.NewGuid():N}";
        var dep = await _scheduler.ScheduleDependentAsyncUntyped(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            childId,
            new SchedulerTestInput(),
            parentId
        );

        dep.ScheduleType.Should().Be(ScheduleType.Dependent);
        dep.DependsOnManifestId.Should().NotBeNull();
    }

    [Test]
    public async Task ScheduleDependentAsyncUntyped_MissingParent_Throws()
    {
        var act = async () =>
            await _scheduler.ScheduleDependentAsyncUntyped(
                typeof(SchedulerTestTrain),
                typeof(SchedulerTestInput),
                "child",
                new SchedulerTestInput(),
                "missing-parent"
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public async Task ScheduleManyAsyncUntyped_EmptySources_ReturnsEmptyWithoutTransaction()
    {
        var result = await _scheduler.ScheduleManyAsyncUntyped<string>(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            sources: Array.Empty<string>(),
            map: s => (s, new SchedulerTestInput { Value = s }),
            schedule: Interval
        );

        result.Should().BeEmpty();
    }

    [Test]
    public async Task ScheduleManyAsyncUntyped_WithSources_PersistsAllAndCommits()
    {
        var prefix = $"u-many-{Guid.NewGuid():N}";
        var sources = new[] { "a", "b", "c" };

        var result = await _scheduler.ScheduleManyAsyncUntyped<string>(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            sources: sources,
            map: s => ($"{prefix}-{s}", new SchedulerTestInput { Value = s }),
            schedule: Interval,
            options: o => o.PrunePrefix(prefix)
        );

        result.Should().HaveCount(3);
        DataContext.Reset();
        var stored = await DataContext
            .Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith(prefix))
            .ToListAsync();
        stored.Should().HaveCount(3);
    }

    [Test]
    public async Task ScheduleManyDependentAsyncUntyped_EmptySources_ReturnsEmpty()
    {
        var result = await _scheduler.ScheduleManyDependentAsyncUntyped<string>(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            sources: Array.Empty<string>(),
            map: s => (s, new SchedulerTestInput()),
            dependsOn: _ => "n/a"
        );

        result.Should().BeEmpty();
    }

    [Test]
    public async Task ScheduleManyDependentAsyncUntyped_MissingParent_RollsBack()
    {
        var prefix = $"u-many-dep-{Guid.NewGuid():N}";

        var act = async () =>
            await _scheduler.ScheduleManyDependentAsyncUntyped<string>(
                typeof(SchedulerTestTrain),
                typeof(SchedulerTestInput),
                sources: new[] { "a", "b" },
                map: s => ($"{prefix}-{s}", new SchedulerTestInput()),
                dependsOn: _ => "missing-parent-id"
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");

        DataContext.Reset();
        var leaked = await DataContext
            .Manifests.AsNoTracking()
            .CountAsync(m => m.ExternalId.StartsWith(prefix));
        leaked.Should().Be(0, "the transaction must roll back when a parent is missing");
    }

    [Test]
    public async Task ScheduleManyDependentAsyncUntyped_WithSources_PersistsDependents()
    {
        var prefix = $"u-many-dep-ok-{Guid.NewGuid():N}";
        var parentId = $"{prefix}-parent";

        await _scheduler.ScheduleAsyncUntyped(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            parentId,
            new SchedulerTestInput(),
            Interval
        );

        var result = await _scheduler.ScheduleManyDependentAsyncUntyped<string>(
            typeof(SchedulerTestTrain),
            typeof(SchedulerTestInput),
            sources: new[] { "x", "y" },
            map: s => ($"{prefix}-{s}", new SchedulerTestInput { Value = s }),
            dependsOn: _ => parentId,
            options: o => o.PrunePrefix(prefix)
        );

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(m => m.ScheduleType.Should().Be(ScheduleType.Dependent));
    }

    #endregion

    #region Public ScheduleManyAsync — empty + rollback

    [Test]
    public async Task ScheduleManyAsync_EmptySources_ReturnsEmpty()
    {
        var result = await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit,
            string
        >(
            sources: Array.Empty<string>(),
            map: s => (s, new SchedulerTestInput { Value = s }),
            schedule: Interval
        );

        result.Should().BeEmpty();
    }

    [Test]
    public async Task ScheduleManyDependentAsync_EmptySources_ReturnsEmpty()
    {
        var result = await _scheduler.ScheduleManyDependentAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit,
            string
        >(
            sources: Array.Empty<string>(),
            map: s => (s, new SchedulerTestInput()),
            dependsOn: _ => "x"
        );

        result.Should().BeEmpty();
    }

    [Test]
    public async Task ScheduleManyDependentAsync_MissingParent_RollsBack()
    {
        var prefix = $"typed-many-dep-{Guid.NewGuid():N}";

        var act = async () =>
            await _scheduler.ScheduleManyDependentAsync<
                ISchedulerTestTrain,
                SchedulerTestInput,
                Unit,
                string
            >(
                sources: new[] { "a" },
                map: s => ($"{prefix}-{s}", new SchedulerTestInput()),
                dependsOn: _ => "missing-typed-parent"
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        DataContext.Reset();
        (await DataContext.Manifests.CountAsync(m => m.ExternalId.StartsWith(prefix)))
            .Should()
            .Be(0);
    }

    [Test]
    public async Task ScheduleDependentAsync_MissingParent_Throws()
    {
        var act = async () =>
            await _scheduler.ScheduleDependentAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "typed-child",
                new SchedulerTestInput(),
                "missing-typed-parent"
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public async Task ScheduleManyAsync_WithBadVariance_RollsBackTransaction()
    {
        // Variance < 0 throws inside UpsertManifestAsync; the catch path rolls back the tx.
        var prefix = $"typed-many-rb-{Guid.NewGuid():N}";

        var act = async () =>
            await _scheduler.ScheduleManyAsync<
                ISchedulerTestTrain,
                SchedulerTestInput,
                Unit,
                string
            >(
                sources: new[] { "a", "b" },
                map: s => ($"{prefix}-{s}", new SchedulerTestInput()),
                schedule: Interval with
                {
                    Variance = TimeSpan.FromSeconds(-1),
                }
            );

        await act.Should().ThrowAsync<InvalidOperationException>();

        DataContext.Reset();
        (await DataContext.Manifests.CountAsync(m => m.ExternalId.StartsWith(prefix)))
            .Should()
            .Be(0);
    }

    #endregion

    #region TriggerGroupAsync — empty short-circuit

    [Test]
    public async Task TriggerGroupAsync_NoMatchingManifests_ReturnsZero()
    {
        var count = await _scheduler.TriggerGroupAsync(groupId: -9999);
        count.Should().Be(0);
    }

    [Test]
    public async Task TriggerGroupAsync_DependentManifestsExcluded_OnlyTriggersScheduled()
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"trig-group-{Guid.NewGuid():N}"
        );

        // Independent (will be triggered)
        var indep = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "indep" },
            }
        );
        indep.ExternalId = $"indep-{Guid.NewGuid():N}";
        indep.ManifestGroupId = group.Id;

        // Dependent (must be skipped)
        var dep = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Dependent,
                Properties = new SchedulerTestInput { Value = "dep" },
            }
        );
        dep.ExternalId = $"dep-{Guid.NewGuid():N}";
        dep.ManifestGroupId = group.Id;

        await DataContext.Track(indep);
        await DataContext.Track(dep);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var triggered = await _scheduler.TriggerGroupAsync(group.Id);
        triggered.Should().Be(1);

        var queued = await DataContext
            .WorkQueues.AsNoTracking()
            .Where(w => w.ManifestId == indep.Id || w.ManifestId == dep.Id)
            .ToListAsync();
        queued.Should().HaveCount(1);
        queued[0].ManifestId.Should().Be(indep.Id);
    }

    #endregion

    #region CancelAsync / CancelGroupAsync — no in-progress short-circuit

    [Test]
    public async Task CancelAsync_NoInProgress_ReturnsZero()
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"cancel-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput(),
            }
        );
        manifest.ExternalId = $"cancel-noip-{Guid.NewGuid():N}";
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var cancelled = await _scheduler.CancelAsync(manifest.ExternalId);
        cancelled.Should().Be(0);
    }

    [Test]
    public async Task CancelGroupAsync_NoInProgress_ReturnsZero()
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"cg-{Guid.NewGuid():N}"
        );
        var cancelled = await _scheduler.CancelGroupAsync(group.Id);
        cancelled.Should().Be(0);
    }

    #endregion
}
