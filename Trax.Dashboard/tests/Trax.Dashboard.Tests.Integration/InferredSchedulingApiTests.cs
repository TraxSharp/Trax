using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Dashboard.Tests.Integration.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Dashboard.Tests.Integration;

[TestFixture]
public class InferredSchedulingApiTests
{
    private IServiceCollection _services = null!;
    private Trax.CoreEffectConfigurationBuilder _parentBuilder = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new ServiceCollection();
        _parentBuilder = new Trax.CoreEffectConfigurationBuilder(_services);
    }

    #region Single-type-param: Schedule, Include, ThenInclude

    [Test]
    public void Schedule_SingleTypeParam_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.Schedule<IFakeSchedulerWorkflowA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5)
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void Schedule_ThenInclude_SingleTypeParam_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .Schedule<IFakeSchedulerWorkflowA>(
                            "job-a",
                            new FakeManifestInputA(),
                            Every.Minutes(5)
                        )
                        .ThenInclude<IFakeSchedulerWorkflowB>("job-b", new FakeManifestInputB())
            );

        act.Should().NotThrow();
    }

    [Test]
    public void Schedule_Include_FanOut_SingleTypeParam_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .Schedule<IFakeSchedulerWorkflowA>(
                            "job-a",
                            new FakeManifestInputA(),
                            Every.Minutes(5)
                        )
                        .Include<IFakeSchedulerWorkflowB>("job-b", new FakeManifestInputB())
                        .Include<IFakeSchedulerWorkflowC>("job-c", new FakeManifestInputC())
            );

        act.Should().NotThrow();
    }

    [Test]
    public void Schedule_WithOptions_SingleTypeParam_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.Schedule<IFakeSchedulerWorkflowA>(
                        "job-a",
                        new FakeManifestInputA(),
                        Every.Minutes(5),
                        options =>
                            options.Priority(10).Group("my-group", group => group.MaxActiveJobs(5))
                    )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Input type validation

    [Test]
    public void Schedule_WrongInputType_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.Schedule<IFakeSchedulerWorkflowA>(
                        "job-a",
                        new FakeManifestInputB(), // Wrong: WorkflowA expects FakeManifestInputA
                        Every.Minutes(5)
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Input type mismatch*FakeManifestInputA*FakeManifestInputB*");
    }

    [Test]
    public void Include_WrongInputType_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .Schedule<IFakeSchedulerWorkflowA>(
                            "job-a",
                            new FakeManifestInputA(),
                            Every.Minutes(5)
                        )
                        .Include<IFakeSchedulerWorkflowB>(
                            "job-b",
                            new FakeManifestInputA() // Wrong: WorkflowB expects FakeManifestInputB
                        )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Input type mismatch*FakeManifestInputB*FakeManifestInputA*");
    }

    [Test]
    public void ThenInclude_WrongInputType_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .Schedule<IFakeSchedulerWorkflowA>(
                            "job-a",
                            new FakeManifestInputA(),
                            Every.Minutes(5)
                        )
                        .ThenInclude<IFakeSchedulerWorkflowB>(
                            "job-b",
                            new FakeManifestInputC() // Wrong: WorkflowB expects FakeManifestInputB
                        )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Input type mismatch*FakeManifestInputB*FakeManifestInputC*");
    }

    #endregion

    #region Ordering validation

    [Test]
    public void ThenInclude_WithoutSchedule_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.ThenInclude<IFakeSchedulerWorkflowA>(
                        "job-a",
                        new FakeManifestInputA()
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ThenInclude*must be called after*Schedule*");
    }

    [Test]
    public void Include_WithoutSchedule_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.Include<IFakeSchedulerWorkflowA>("job-a", new FakeManifestInputA())
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Include*must be called after*Schedule*");
    }

    #endregion

    #region Batch: ScheduleMany with ManifestItem

    [Test]
    public void ScheduleMany_Named_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.ScheduleMany<IFakeSchedulerWorkflowA>(
                        "batch-a",
                        Enumerable
                            .Range(0, 5)
                            .Select(i => new ManifestItem($"{i}", new FakeManifestInputA())),
                        Every.Minutes(5)
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void ScheduleMany_Unnamed_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.ScheduleMany<IFakeSchedulerWorkflowA>(
                        Enumerable
                            .Range(0, 3)
                            .Select(
                                i => new ManifestItem($"batch-item-{i}", new FakeManifestInputA())
                            ),
                        Every.Minutes(5)
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void ScheduleMany_WrongInputType_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.ScheduleMany<IFakeSchedulerWorkflowA>(
                        "batch-a",
                        Enumerable
                            .Range(0, 3)
                            .Select(
                                i =>
                                    new ManifestItem(
                                        $"{i}",
                                        new FakeManifestInputB() // Wrong type
                                    )
                            ),
                        Every.Minutes(5)
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Input type mismatch*FakeManifestInputA*FakeManifestInputB*");
    }

    #endregion

    #region Batch: IncludeMany with ManifestItem

    [Test]
    public void IncludeMany_RootBased_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .Schedule<IFakeSchedulerWorkflowA>(
                            "root-job",
                            new FakeManifestInputA(),
                            Every.Minutes(5)
                        )
                        .IncludeMany<IFakeSchedulerWorkflowB>(
                            Enumerable
                                .Range(0, 5)
                                .Select(
                                    i =>
                                        new ManifestItem($"dependent-{i}", new FakeManifestInputB())
                                )
                        )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void IncludeMany_Named_WithDependsOn_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .ScheduleMany<IFakeSchedulerWorkflowA>(
                            "extract",
                            Enumerable
                                .Range(0, 3)
                                .Select(i => new ManifestItem($"{i}", new FakeManifestInputA())),
                            Every.Minutes(5)
                        )
                        .IncludeMany<IFakeSchedulerWorkflowB>(
                            "transform",
                            Enumerable
                                .Range(0, 3)
                                .Select(
                                    i =>
                                        new ManifestItem(
                                            $"{i}",
                                            new FakeManifestInputB(),
                                            DependsOn: $"extract-{i}"
                                        )
                                )
                        )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void IncludeMany_WithoutSchedule_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler.IncludeMany<IFakeSchedulerWorkflowA>(
                        Enumerable
                            .Range(0, 3)
                            .Select(i => new ManifestItem($"item-{i}", new FakeManifestInputA()))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IncludeMany*must be called after*Schedule*");
    }

    #endregion

    #region Batch: ThenIncludeMany with ManifestItem

    [Test]
    public void ThenIncludeMany_WithDependsOn_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .ScheduleMany<IFakeSchedulerWorkflowA>(
                            "extract",
                            Enumerable
                                .Range(0, 3)
                                .Select(i => new ManifestItem($"{i}", new FakeManifestInputA())),
                            Every.Minutes(5)
                        )
                        .IncludeMany<IFakeSchedulerWorkflowB>(
                            "transform",
                            Enumerable
                                .Range(0, 3)
                                .Select(
                                    i =>
                                        new ManifestItem(
                                            $"{i}",
                                            new FakeManifestInputB(),
                                            DependsOn: $"extract-{i}"
                                        )
                                )
                        )
                        .ThenIncludeMany<IFakeSchedulerWorkflowC>(
                            "load",
                            Enumerable
                                .Range(0, 3)
                                .Select(
                                    i =>
                                        new ManifestItem(
                                            $"{i}",
                                            new FakeManifestInputC(),
                                            DependsOn: $"transform-{i}"
                                        )
                                )
                        )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void ThenIncludeMany_MissingDependsOn_ThrowsInvalidOperationException()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .ScheduleMany<IFakeSchedulerWorkflowA>(
                            "extract",
                            Enumerable
                                .Range(0, 3)
                                .Select(i => new ManifestItem($"{i}", new FakeManifestInputA())),
                            Every.Minutes(5)
                        )
                        .ThenIncludeMany<IFakeSchedulerWorkflowB>(
                            Enumerable
                                .Range(0, 3)
                                .Select(
                                    i =>
                                        new ManifestItem(
                                            $"item-{i}",
                                            new FakeManifestInputB()
                                        // No DependsOn — should fail
                                        )
                                )
                        )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ThenIncludeMany*requires DependsOn*");
    }

    #endregion

    #region ManifestItem with Dormant option

    [Test]
    public void IncludeMany_WithDormantOption_Succeeds()
    {
        var act = () =>
            _parentBuilder.AddScheduler(
                scheduler =>
                    scheduler
                        .ScheduleMany<IFakeSchedulerWorkflowA>(
                            "extract",
                            Enumerable
                                .Range(0, 3)
                                .Select(i => new ManifestItem($"{i}", new FakeManifestInputA())),
                            Every.Minutes(5)
                        )
                        .IncludeMany<IFakeSchedulerWorkflowB>(
                            "dq-check",
                            Enumerable
                                .Range(0, 3)
                                .Select(
                                    i =>
                                        new ManifestItem(
                                            $"{i}",
                                            new FakeManifestInputB(),
                                            DependsOn: $"extract-{i}"
                                        )
                                ),
                            options: o => o.Dormant()
                        )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Cycle detection with new API

    [Test]
    public void IncludeMany_CrossGroupCycle_ThrowsInvalidOperationException()
    {
        // group-a → group-b (via IncludeMany DependsOn) and group-b → group-a
        var act = () =>
            _parentBuilder.AddScheduler(scheduler =>
            {
                scheduler
                    .ScheduleMany<IFakeSchedulerWorkflowA>(
                        "group-a",
                        Enumerable
                            .Range(0, 2)
                            .Select(i => new ManifestItem($"{i}", new FakeManifestInputA())),
                        Every.Minutes(5)
                    )
                    .IncludeMany<IFakeSchedulerWorkflowB>(
                        "group-b",
                        Enumerable
                            .Range(0, 2)
                            .Select(
                                i =>
                                    new ManifestItem(
                                        $"{i}",
                                        new FakeManifestInputB(),
                                        DependsOn: $"group-a-{i}"
                                    )
                            )
                    );

                // Close the cycle: group-b → group-a
                scheduler
                    .Schedule<IFakeSchedulerWorkflowC>(
                        "group-b-root",
                        new FakeManifestInputC(),
                        Every.Minutes(5),
                        options => options.Group("group-b")
                    )
                    .ThenInclude<IFakeSchedulerWorkflowD>(
                        "group-a-root",
                        new FakeManifestInputD(),
                        options => options.Group("group-a")
                    );
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Circular dependency*");
    }

    #endregion
}
