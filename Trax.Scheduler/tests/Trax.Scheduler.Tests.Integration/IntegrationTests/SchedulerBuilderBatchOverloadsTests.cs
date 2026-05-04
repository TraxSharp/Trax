using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// E2E coverage of the explicit (TTrain, TInput, TOutput, TSource) BatchScheduling overloads —
/// ScheduleMany, ScheduleMany name-based, IncludeMany (with and without per-item dependsOn).
/// The InferredScheduling tests already cover the TTrain-only variants.
/// </summary>
[TestFixture]
public class SchedulerBuilderBatchOverloadsTests
{
    [Test]
    public async Task ScheduleMany_ExplicitTypes_MaterialisesEachItem()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                new[] { "x", "y", "z" },
                src => ($"explicit-{src}", new SchedulerTestInput { Value = src }),
                Every.Minutes(5)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("explicit-"))
            .ToListAsync();
        manifests.Should().HaveCount(3);
    }

    [Test]
    public async Task ScheduleMany_ExplicitTypes_NameBased_AppliesPrefix()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                "ingest",
                new[] { "users", "orders" },
                src => (src, new SchedulerTestInput()),
                Every.Minutes(5)
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("ingest-"))
            .ToListAsync();
        manifests
            .Select(m => m.ExternalId)
            .Should()
            .BeEquivalentTo("ingest-users", "ingest-orders");
    }

    [Test]
    public async Task IncludeMany_ExplicitTypes_AfterSchedule_QueuesDependents()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "exp-root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .IncludeMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    new[] { "a", "b" },
                    src => ($"exp-dep-{src}", new SchedulerTestInput { Value = src })
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("exp-"))
            .ToListAsync();

        var root = manifests.First(m => m.ExternalId == "exp-root");
        var deps = manifests.Where(m => m.ExternalId.StartsWith("exp-dep-")).ToList();
        deps.Should().HaveCount(2);
        deps.Should().AllSatisfy(d => d.DependsOnManifestId.Should().Be(root.Id));
    }

    [Test]
    public async Task IncludeMany_ExplicitTypes_PerItemDependsOnFunc_RespectsExplicitParent()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "func-root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .IncludeMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    new[] { "a", "b" },
                    src => ($"func-{src}", new SchedulerTestInput()),
                    dependsOn: src => "func-root"
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("func-"))
            .ToListAsync();
        var root = manifests.First(m => m.ExternalId == "func-root");
        var deps = manifests.Where(m => m.ExternalId != "func-root").ToList();
        deps.Should().AllSatisfy(d => d.DependsOnManifestId.Should().Be(root.Id));
    }

    [Test]
    public async Task IncludeMany_ExplicitTypes_NameBased_AppliesGroupAndPrefix()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "name-root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .IncludeMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    "process",
                    new[] { "a", "b" },
                    src => (src, new SchedulerTestInput())
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Include(m => m.ManifestGroup)
            .Where(m => m.ExternalId.StartsWith("process-"))
            .ToListAsync();

        manifests.Select(m => m.ExternalId).Should().BeEquivalentTo("process-a", "process-b");
        manifests.Should().AllSatisfy(m => m.ManifestGroup.Name.Should().Be("process"));
    }

    [Test]
    public async Task ThenIncludeMany_ExplicitTypes_PerItemDependsOn_QueuesDependents()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "ti-root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "ti-mid",
                    new SchedulerTestInput()
                )
                .ThenIncludeMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    new[] { "x", "y" },
                    src => ($"ti-leaf-{src}", new SchedulerTestInput()),
                    dependsOn: src => "ti-mid"
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("ti-"))
            .ToListAsync();
        var mid = manifests.First(m => m.ExternalId == "ti-mid");
        var leafs = manifests.Where(m => m.ExternalId.StartsWith("ti-leaf-")).ToList();
        leafs.Should().HaveCount(2);
        leafs.Should().AllSatisfy(l => l.DependsOnManifestId.Should().Be(mid.Id));
    }

    [Test]
    public async Task ThenIncludeMany_Inferred_RequiresDependsOnOnEachItem()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("tim-root", new SchedulerTestInput(), Every.Minutes(5))
                .Include<ISchedulerTestTrain>("tim-mid", new SchedulerTestInput())
                .ThenIncludeMany<ISchedulerTestTrain>(
                    new[]
                    {
                        new ManifestItem("tim-leaf-a", new SchedulerTestInput())
                        {
                            DependsOn = "tim-mid",
                        },
                        new ManifestItem("tim-leaf-b", new SchedulerTestInput())
                        {
                            DependsOn = "tim-mid",
                        },
                    }
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("tim-"))
            .ToListAsync();
        manifests.Should().HaveCount(4);
    }

    [Test]
    public void ThenIncludeMany_Inferred_MissingDependsOn_Throws()
    {
        Action act = () =>
            SchedulerE2EFixture.CreateInMemory(s =>
                s.Schedule<ISchedulerTestTrain>(
                        "missing-root",
                        new SchedulerTestInput(),
                        Every.Minutes(5)
                    )
                    .ThenIncludeMany<ISchedulerTestTrain>(
                        new[] { new ManifestItem("orphan", new SchedulerTestInput()) }
                    )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*DependsOn*");
    }

    [Test]
    public async Task IncludeMany_Inferred_NameBased_AppliesPrefixAndGroup()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("im-root", new SchedulerTestInput(), Every.Minutes(5))
                .IncludeMany<ISchedulerTestTrain>(
                    "process",
                    new[]
                    {
                        new ManifestItem("a", new SchedulerTestInput()),
                        new ManifestItem("b", new SchedulerTestInput()),
                    }
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Include(m => m.ManifestGroup)
            .Where(m => m.ExternalId.StartsWith("process-"))
            .ToListAsync();
        manifests.Should().HaveCount(2);
        manifests.Should().AllSatisfy(m => m.ManifestGroup.Name.Should().Be("process"));
    }

    [Test]
    public async Task ThenIncludeMany_NameBased_AppliesPrefixAndDependsOn()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "tn-root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "tn-mid",
                    new SchedulerTestInput()
                )
                .ThenIncludeMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    "leaves",
                    new[] { "a", "b" },
                    src => (src, new SchedulerTestInput()),
                    dependsOn: src => "tn-mid"
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Include(m => m.ManifestGroup)
            .Where(m => m.ExternalId.StartsWith("leaves-"))
            .ToListAsync();

        manifests.Should().HaveCount(2);
        manifests.Select(m => m.ExternalId).Should().BeEquivalentTo("leaves-a", "leaves-b");
        manifests.Should().AllSatisfy(m => m.ManifestGroup.Name.Should().Be("leaves"));
    }

    [Test]
    public async Task IncludeMany_NameBasedWithExplicitDependsOn_AppliesPrefixAndParent()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "in-root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .IncludeMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                    "deps",
                    new[] { "a", "b" },
                    src => (src, new SchedulerTestInput()),
                    dependsOn: src => "in-root"
                )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Include(m => m.ManifestGroup)
            .Where(m => m.ExternalId.StartsWith("deps-"))
            .ToListAsync();

        manifests.Should().HaveCount(2);
        manifests.Should().AllSatisfy(m => m.ManifestGroup.Name.Should().Be("deps"));
    }

    [Test]
    public async Task ScheduleMany_ConfigureEach_AppliesPerItemManifestOptions()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, int>(
                new[] { 1, 2, 3 },
                i => ($"each-{i}", new SchedulerTestInput()),
                Every.Minutes(5),
                configureEach: (i, manifestOpts) => manifestOpts.Priority = i
            )
        );

        await fx.MaterializePendingManifestsAsync();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("each-"))
            .OrderBy(m => m.ExternalId)
            .ToListAsync();
        manifests.Select(m => m.Priority).Should().Equal(1, 2, 3);
    }
}
