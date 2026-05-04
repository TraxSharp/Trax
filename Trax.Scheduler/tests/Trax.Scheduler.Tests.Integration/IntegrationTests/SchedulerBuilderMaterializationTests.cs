using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// End-to-end coverage of the SchedulerConfigurationBuilder ScheduleFunc lambdas — the bits
/// that don't run from unit tests because they live inside async closures captured at
/// configuration time and only execute when SchedulerStartupService materialises the queued
/// PendingManifests at host startup.
///
/// Each test builds its own scheduler config, materialises it via the fixture, and asserts
/// on the resulting Manifest / ManifestGroup rows in Postgres.
/// </summary>
[TestFixture]
public class SchedulerBuilderMaterializationTests
{
    [Test]
    public async Task Schedule_Inferred_MaterialisesIntoManifestRow()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "ext-schedule",
                new SchedulerTestInput { Value = "hi" },
                Every.Minutes(5)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == "ext-schedule");
        manifest.Should().NotBeNull();
        manifest!.IsEnabled.Should().BeTrue();
        manifest.ScheduleType.Should().Be(ScheduleType.Interval);
        manifest.IntervalSeconds.Should().Be(300);
    }

    [Test]
    public async Task Schedule_Explicit_MaterialisesIntoManifestRow()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "ext-explicit",
                new SchedulerTestInput { Value = "hi" },
                Every.Minutes(10)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == "ext-explicit");
        manifest.Should().NotBeNull();
        manifest!.IntervalSeconds.Should().Be(600);
    }

    [Test]
    public async Task ScheduleOnce_Inferred_MaterialisesAsOnceManifestWithScheduledAt()
    {
        var before = DateTime.UtcNow;
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleOnce<ISchedulerTestTrain>(
                "ext-once",
                new SchedulerTestInput(),
                TimeSpan.FromMinutes(5)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == "ext-once");
        manifest.Should().NotBeNull();
        manifest!.ScheduleType.Should().Be(ScheduleType.Once);
        manifest.ScheduledAt.Should().NotBeNull();
        manifest.ScheduledAt!.Value.Should().BeAfter(before);
    }

    [Test]
    public async Task Include_Inferred_LinksDependentToRoot()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .Include<ISchedulerTestTrain>("child", new SchedulerTestInput())
        );

        await fx.MaterializePendingManifestsAsync();

        var root = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "root");
        var child = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "child");
        child.DependsOnManifestId.Should().Be(root.Id);
    }

    [Test]
    public async Task ThenInclude_Inferred_LinksDependentToPriorManifest()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .ThenInclude<ISchedulerTestTrain>("child", new SchedulerTestInput())
                .ThenInclude<ISchedulerTestTrain>("grandchild", new SchedulerTestInput())
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => new[] { "root", "child", "grandchild" }.Contains(m.ExternalId))
            .ToListAsync();
        manifests.Should().HaveCount(3);
        var root = manifests.First(m => m.ExternalId == "root");
        var child = manifests.First(m => m.ExternalId == "child");
        var grand = manifests.First(m => m.ExternalId == "grandchild");
        child.DependsOnManifestId.Should().Be(root.Id);
        grand.DependsOnManifestId.Should().Be(child.Id);
    }

    [Test]
    public async Task ScheduleMany_Inferred_MaterialisesEachItem()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleMany<ISchedulerTestTrain>(
                new[]
                {
                    new ManifestItem("batch-a", new SchedulerTestInput { Value = "a" }),
                    new ManifestItem("batch-b", new SchedulerTestInput { Value = "b" }),
                    new ManifestItem("batch-c", new SchedulerTestInput { Value = "c" }),
                },
                Every.Minutes(5)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("batch-"))
            .ToListAsync();
        manifests.Should().HaveCount(3);
        manifests
            .Select(m => m.ExternalId)
            .Should()
            .BeEquivalentTo("batch-a", "batch-b", "batch-c");
    }

    [Test]
    public async Task ScheduleMany_NameBased_AppliesPrefixToExternalIds()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleMany<ISchedulerTestTrain>(
                "sync",
                new[]
                {
                    new ManifestItem("users", new SchedulerTestInput()),
                    new ManifestItem("orders", new SchedulerTestInput()),
                },
                Every.Minutes(5)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("sync-"))
            .ToListAsync();
        manifests.Select(m => m.ExternalId).Should().BeEquivalentTo("sync-users", "sync-orders");
    }

    [Test]
    public async Task IncludeMany_Inferred_AllItemsDependOnRootByDefault()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .IncludeMany<ISchedulerTestTrain>(
                    new[]
                    {
                        new ManifestItem("dep-a", new SchedulerTestInput()),
                        new ManifestItem("dep-b", new SchedulerTestInput()),
                    }
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var root = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "root");
        var deps = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("dep-"))
            .ToListAsync();

        deps.Should().HaveCount(2);
        deps.Should().AllSatisfy(d => d.DependsOnManifestId.Should().Be(root.Id));
    }

    [Test]
    public async Task IncludeMany_PerItemDependsOn_RespectsExplicitParent()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .IncludeMany<ISchedulerTestTrain>(
                    new[]
                    {
                        new ManifestItem("child-a", new SchedulerTestInput())
                        {
                            DependsOn = "root",
                        },
                        new ManifestItem("child-b", new SchedulerTestInput())
                        {
                            DependsOn = "root",
                        },
                    }
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx.DataContext.Manifests.AsNoTracking().ToListAsync();
        var root = manifests.First(m => m.ExternalId == "root");
        var ca = manifests.First(m => m.ExternalId == "child-a");
        var cb = manifests.First(m => m.ExternalId == "child-b");

        ca.DependsOnManifestId.Should().Be(root.Id);
        cb.DependsOnManifestId.Should().Be(root.Id);
    }

    [Test]
    public async Task Schedule_WithGroupOptions_MaterialisesGroupSettings()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "ext-grouped",
                new SchedulerTestInput(),
                Every.Minutes(5),
                opts => opts.Group("my-group", g => g.MaxActiveJobs(7).Priority(3))
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .Include(m => m.ManifestGroup)
            .FirstAsync(m => m.ExternalId == "ext-grouped");

        manifest.ManifestGroup.Name.Should().Be("my-group");
        manifest.ManifestGroup.MaxActiveJobs.Should().Be(7);
        manifest.ManifestGroup.Priority.Should().Be(3);
    }

    [Test]
    public async Task Schedule_WithCronExpression_StoresCronManifest()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "ext-cron",
                new SchedulerTestInput(),
                Schedule.FromCron("0 3 * * *")
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "ext-cron");

        manifest.ScheduleType.Should().Be(ScheduleType.Cron);
        manifest.CronExpression.Should().Be("0 3 * * *");
    }
}
