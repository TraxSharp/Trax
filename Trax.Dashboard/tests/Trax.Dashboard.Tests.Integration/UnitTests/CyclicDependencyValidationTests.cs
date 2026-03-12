using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Tests.Integration.Fakes.Trains;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Configuration;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class CyclicDependencyValidationTests
{
    private IServiceCollection _services = null!;
    private TraxBuilderWithMediator _parentBuilder = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection();
        var root = new TraxBuilder(_services, new EffectRegistry()) { HasDataProvider = true };
        _parentBuilder = root.AddEffects(effects => effects)
            .AddMediator(typeof(IFakeSchedulerTrainA).Assembly);
    }

    #region Valid DAGs (no cycles)

    [Test]
    public void Build_NoDependencies_Succeeds()
    {
        // Arrange & Act
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler.Schedule<IFakeSchedulerTrainA>(
                    "job-a",
                    new FakeManifestInputA(),
                    Every.Minutes(5)
                )
            );

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public void Build_LinearChainDifferentGroups_Succeeds()
    {
        // Arrange: group-a → group-b → group-c
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler
                    .Schedule<IFakeSchedulerTrainA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5),
                        options => options.Group("group-a")
                    )
                    .ThenInclude<IFakeSchedulerTrainB>(
                        "job-b",
                        new FakeManifestInputB(),
                        options => options.Group("group-b")
                    )
                    .ThenInclude<IFakeSchedulerTrainC>(
                        "job-c",
                        new FakeManifestInputC(),
                        options => options.Group("group-c")
                    )
            );

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public void Build_WithinGroupDependency_Succeeds()
    {
        // Arrange: Both jobs in the same group — same-group edges should not trigger validation
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler
                    .Schedule<IFakeSchedulerTrainA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5),
                        options => options.Group("same-group")
                    )
                    .ThenInclude<IFakeSchedulerTrainB>(
                        "job-b",
                        new FakeManifestInputB(),
                        options => options.Group("same-group")
                    )
            );

        // Assert
        act.Should().NotThrow();
    }

    [Test]
    public void Build_DiamondDagDifferentGroups_Succeeds()
    {
        // Arrange: group-a → group-b, group-a → group-c (via separate chains)
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler
                    .Schedule<IFakeSchedulerTrainA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5),
                        options => options.Group("group-a")
                    )
                    .ThenInclude<IFakeSchedulerTrainB>(
                        "job-b",
                        new FakeManifestInputB(),
                        options => options.Group("group-b")
                    )
                    .Schedule<IFakeSchedulerTrainC>(
                        "job-c",
                        new FakeManifestInputC(),
                        Every.Minutes(5),
                        options => options.Group("group-a")
                    )
                    .ThenInclude<IFakeSchedulerTrainD>(
                        "job-d",
                        new FakeManifestInputD(),
                        options => options.Group("group-c")
                    )
            );

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Cyclic Dependencies (should throw)

    [Test]
    public void Build_TwoGroupCycle_ThrowsInvalidOperationException()
    {
        // Arrange: group-a → group-b and group-b → group-a
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler
                    // Chain 1: group-a → group-b
                    .Schedule<IFakeSchedulerTrainA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5),
                        options => options.Group("group-a")
                    )
                    .ThenInclude<IFakeSchedulerTrainB>(
                        "job-b",
                        new FakeManifestInputB(),
                        options => options.Group("group-b")
                    )
                    // Chain 2: group-b → group-a (creates cycle)
                    .Schedule<IFakeSchedulerTrainC>(
                        "job-c",
                        new FakeManifestInputC(),
                        Every.Minutes(5),
                        options => options.Group("group-b")
                    )
                    .ThenInclude<IFakeSchedulerTrainD>(
                        "job-d",
                        new FakeManifestInputD(),
                        options => options.Group("group-a")
                    )
            );

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*manifest groups*");
    }

    [Test]
    public void Build_ThreeGroupCycle_ThrowsAndListsCycleMembers()
    {
        // Arrange: group-a → group-b → group-c → group-a
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler
                    .Schedule<IFakeSchedulerTrainA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5),
                        options => options.Group("group-a")
                    )
                    .ThenInclude<IFakeSchedulerTrainB>(
                        "job-b",
                        new FakeManifestInputB(),
                        options => options.Group("group-b")
                    )
                    .ThenInclude<IFakeSchedulerTrainC>(
                        "job-c",
                        new FakeManifestInputC(),
                        options => options.Group("group-c")
                    )
                    // Close the cycle: group-c → group-a
                    .Schedule<IFakeSchedulerTrainD>(
                        "job-d",
                        new FakeManifestInputD(),
                        Every.Minutes(5),
                        options => options.Group("group-c")
                    )
                    .ThenInclude<IFakeSchedulerTrainA>(
                        "job-a2",
                        new FakeManifestInputA(),
                        options => options.Group("group-a")
                    )
            );

        // Assert
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency*")
            .WithMessage("*group-a*")
            .WithMessage("*group-b*")
            .WithMessage("*group-c*");
    }

    [Test]
    public void Build_CycleWithDefaultGroupIds_ThrowsInvalidOperationException()
    {
        // Arrange: When groupId is null, externalId becomes the group.
        // "ext-a" → "ext-b" and "ext-b" → "ext-a" (cycle via default group IDs)
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
                scheduler
                    .Schedule<IFakeSchedulerTrainA>(
                        "ext-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5)
                    )
                    .ThenInclude<IFakeSchedulerTrainB>("ext-b", new FakeManifestInputB())
                    .Schedule<IFakeSchedulerTrainC>(
                        "ext-b",
                        new FakeManifestInputC(),
                        Every.Minutes(5)
                    )
                    .ThenInclude<IFakeSchedulerTrainD>("ext-a", new FakeManifestInputD())
            );

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Circular dependency*");
    }

    #endregion
}
