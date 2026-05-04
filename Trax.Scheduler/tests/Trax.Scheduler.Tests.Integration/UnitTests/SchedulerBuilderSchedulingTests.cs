using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Direct tests for SchedulerConfigurationBuilder Schedule / ScheduleOnce / ThenInclude /
/// Include / ScheduleMany surface that build PendingManifest entries. Verifies the queue
/// shape, dependency edges, and the ThenInclude/Include validation guards.
/// </summary>
[TestFixture]
public class SchedulerBuilderSchedulingTests
{
    private SchedulerConfiguration ResolveConfiguration(
        Action<SchedulerConfigurationBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                {
                    scheduler.UseInMemoryWorkers();
                    configure(scheduler);
                    return scheduler;
                })
        );
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<SchedulerConfiguration>();
    }

    #region Schedule

    [Test]
    public void Schedule_Explicit3TypeArgs_QueuesPendingManifest()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "ext-1",
                new SchedulerTestInput(),
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExternalId.Should().Be("ext-1");
    }

    [Test]
    public void Schedule_WithOptions_AppliesGroupId()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "ext-grouped",
                new SchedulerTestInput(),
                Every.Minutes(5),
                opts => opts.Group("my-group", g => g.MaxActiveJobs(3))
            )
        );

        config.PendingManifests.Should().ContainSingle(m => m.ExternalId == "ext-grouped");
    }

    #endregion

    #region ScheduleOnce

    [Test]
    public void ScheduleOnce_Explicit3TypeArgs_QueuesPendingManifest()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleOnce<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                "once-1",
                new SchedulerTestInput(),
                TimeSpan.FromMinutes(1)
            )
        );

        config.PendingManifests.Should().ContainSingle(m => m.ExternalId == "once-1");
    }

    [Test]
    public void ScheduleOnce_Inferred_QueuesPendingManifest()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleOnce<ISchedulerTestTrain>(
                "once-inferred",
                new SchedulerTestInput(),
                TimeSpan.FromMinutes(1)
            )
        );

        config.PendingManifests.Should().ContainSingle(m => m.ExternalId == "once-inferred");
    }

    #endregion

    #region ThenInclude / Include

    [Test]
    public void ThenInclude_AfterSchedule_AddsDependentAfterRoot()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .ThenInclude<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "child",
                    new SchedulerTestInput()
                )
        );

        config.PendingManifests.Select(m => m.ExternalId).Should().Equal("root", "child");
    }

    [Test]
    public void ThenInclude_WithoutPriorSchedule_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.ThenInclude<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "orphan",
                    new SchedulerTestInput()
                )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*ThenInclude*");
    }

    [Test]
    public void Include_AfterSchedule_BranchesFromRoot()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "root",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "branchA",
                    new SchedulerTestInput()
                )
                .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "branchB",
                    new SchedulerTestInput()
                )
        );

        config
            .PendingManifests.Select(m => m.ExternalId)
            .Should()
            .Equal("root", "branchA", "branchB");
    }

    [Test]
    public void Include_WithoutPriorSchedule_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "orphan",
                    new SchedulerTestInput()
                )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Include*");
    }

    [Test]
    public void IncludeThenInclude_Combination_BuildsBranchedDag()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "A",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "B",
                    new SchedulerTestInput()
                )
                .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "C",
                    new SchedulerTestInput()
                )
                .ThenInclude<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                    "D",
                    new SchedulerTestInput()
                )
        );

        config.PendingManifests.Should().HaveCount(4);
    }

    #endregion

    #region ScheduleMany

    [Test]
    public void ScheduleMany_3TypeArgsWithSourceFunc_QueuesSingleBatchManifest()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                new[] { "a", "b", "c" },
                source => ($"item-{source}", new SchedulerTestInput()),
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExpectedExternalIds.Should().HaveCount(3);
    }

    [Test]
    public void ScheduleMany_NameBasedConvention_BuildsExternalIds()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                "sync",
                new[] { "users", "orders" },
                source => (source, new SchedulerTestInput()),
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExpectedExternalIds.Should().HaveCount(2);
        config
            .PendingManifests[0]
            .ExpectedExternalIds.Should()
            .Contain(id => id.StartsWith("sync-"));
    }

    [Test]
    public void ScheduleMany_EmptySource_QueuesEmptyManifest()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                Array.Empty<string>(),
                source => (source, new SchedulerTestInput()),
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExpectedExternalIds.Should().BeEmpty();
    }

    #endregion

    #region Inferred (TTrain only) overloads

    [Test]
    public void Schedule_Inferred_QueuesPendingManifest()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain>(
                "ext-inferred",
                new SchedulerTestInput(),
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().ContainSingle(m => m.ExternalId == "ext-inferred");
    }

    [Test]
    public void Include_Inferred_AddsDependent()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .Include<ISchedulerTestTrain>("branch", new SchedulerTestInput())
        );

        config.PendingManifests.Should().HaveCount(2);
        config.PendingManifests.Select(m => m.ExternalId).Should().Equal("root", "branch");
    }

    [Test]
    public void ThenInclude_Inferred_AddsDependent()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .ThenInclude<ISchedulerTestTrain>("child", new SchedulerTestInput())
        );

        config.PendingManifests.Select(m => m.ExternalId).Should().Equal("root", "child");
    }

    [Test]
    public void Include_Inferred_WithoutSchedule_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.Include<ISchedulerTestTrain>("orphan", new SchedulerTestInput())
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*Include*");
    }

    [Test]
    public void ThenInclude_Inferred_WithoutSchedule_Throws()
    {
        Action act = () =>
            ResolveConfiguration(s =>
                s.ThenInclude<ISchedulerTestTrain>("orphan", new SchedulerTestInput())
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*ThenInclude*");
    }

    [Test]
    public void ScheduleMany_Inferred_ManifestItems_QueuesBatch()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleMany<ISchedulerTestTrain>(
                new[]
                {
                    new ManifestItem("a", new SchedulerTestInput()),
                    new ManifestItem("b", new SchedulerTestInput()),
                },
                Every.Minutes(5)
            )
        );

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExpectedExternalIds.Should().Equal("a", "b");
    }

    [Test]
    public void ScheduleMany_NameBased_AppliesPrefix()
    {
        var config = ResolveConfiguration(s =>
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

        config.PendingManifests.Should().HaveCount(1);
        config.PendingManifests[0].ExpectedExternalIds.Should().Equal("sync-users", "sync-orders");
    }

    [Test]
    public void IncludeMany_Inferred_AfterSchedule_QueuesDependents()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<ISchedulerTestTrain>("root", new SchedulerTestInput(), Every.Minutes(5))
                .IncludeMany<ISchedulerTestTrain>(
                    new[]
                    {
                        new ManifestItem("a", new SchedulerTestInput()),
                        new ManifestItem("b", new SchedulerTestInput()),
                    }
                )
        );

        config.PendingManifests.Should().HaveCount(2);
    }

    #endregion
}
