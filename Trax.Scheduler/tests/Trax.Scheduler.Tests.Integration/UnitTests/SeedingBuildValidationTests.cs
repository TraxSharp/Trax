using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Core.Functional;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// What the builder refuses about manifest groups and named batches, before any row is written.
///
/// <para>Enforces <c>docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md</c>: members of one group may not state different values for the same
/// group setting.</para>
/// </summary>
[Property("adr", "docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md")]
[TestFixture]
public class SeedingBuildValidationTests
{
    [Test]
    public void Two_members_stating_different_limits_for_one_group_fail_the_build_naming_both()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "member-a",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("shared", g => g.MaxActiveJobs(2))
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "member-b",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("shared", g => g.MaxActiveJobs(3))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*shared*MaxActiveJobs*member-a*member-b*",
                "conflicting stated group settings fail the build. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
    }

    [Test]
    public void Two_members_stating_different_group_priorities_fail_the_build()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "p-a",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("prio", g => g.Priority(5))
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "p-b",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("prio", g => g.Priority(9))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*prio*Priority*p-a*p-b*",
                "conflicting stated group settings fail the build. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
    }

    [Test]
    public void Members_that_agree_or_leave_a_group_setting_unstated_build()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "agree-a",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("agreed", g => g.MaxActiveJobs(2).Enabled(true))
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "agree-b",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("agreed", g => g.MaxActiveJobs(2))
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "agree-c",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("agreed")
                    )
            );

        act.Should()
            .NotThrow(
                "agreeing or silent members do not conflict. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
    }

    [Test]
    public void Two_named_batches_sharing_a_group_where_one_name_extends_the_other_fail_the_build()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        "sync-users",
                        ["alice"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.Group("all-sync")
                    )
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        "sync",
                        ["orders"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.Group("all-sync")
                    )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*'sync'*'sync-users'*");
    }

    [Test]
    public void Two_members_stating_different_enabled_states_for_one_group_fail_the_build()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "e-a",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("toggled", g => g.Enabled(true))
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "e-b",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("toggled", g => g.Enabled(false))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'toggled'*Enabled values: True by 'e-a' and False by 'e-b'*");
    }

    [Test]
    public void A_member_stating_no_limit_conflicts_with_one_stating_a_limit_and_is_named_as_none()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "l-a",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("limited", g => g.MaxActiveJobs(null))
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "l-b",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("limited", g => g.MaxActiveJobs(2))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*MaxActiveJobs values: none by 'l-a' and 2 by 'l-b'*");
    }

    [Test]
    public void An_unnamed_batch_without_a_group_or_prefix_is_checked_as_the_group_of_its_first_id()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        ["first-id", "second-id"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.Priority(5)
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "other",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("first-id", g => g.Priority(9))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Manifest group 'first-id'*Priority values: 5 by the batch starting 'first-id' and 9 by 'other'*"
            );
    }

    [Test]
    public void An_unnamed_batch_with_only_a_prune_prefix_is_checked_as_the_group_of_its_prefix()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        ["pp-a"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.PrunePrefix("pp-").Priority(5)
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "other",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("pp-", g => g.Priority(9))
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Manifest group 'pp-'*5 by the batch starting 'pp-a' and 9 by 'other'*");
    }

    [Test]
    public void A_named_batch_placed_in_another_group_does_not_state_that_groups_priority()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        "nightly",
                        ["n1"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.Group("shared").Priority(5)
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "member",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("shared", g => g.Priority(9))
                    )
            );

        act.Should()
            .NotThrow(
                "the batch's Priority(5) is its manifests' own priority; it owns no group here, so "
                    + "only 'member' states the group's"
            );
    }

    [Test]
    public void Unnamed_batches_whose_prune_prefixes_overlap_fail_the_build_even_in_different_groups()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        ["sync-users-alice"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.Group("users").PrunePrefix("sync-users-")
                    )
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        ["sync-orders"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.Group("orders").PrunePrefix("sync-")
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>(
                "an unnamed batch's prune is not limited to its group"
            )
            .WithMessage(
                "The batch starting 'sync-orders' prunes manifests whose external ID starts with 'sync-'*"
            );
    }

    [Test]
    public void A_schedule_inside_a_named_batchs_group_and_prefix_fails_the_build()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        "sync",
                        ["orders"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5)
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "sync-extra",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("sync")
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>(
                "the batch's prune would delete 'sync-extra' and its seed would recreate it every start"
            )
            .WithMessage(
                "Batch 'sync' prunes manifests whose external ID starts with 'sync-'*'sync-extra'*"
            );
    }

    [Test]
    public void A_schedule_matching_an_unnamed_batchs_prune_prefix_fails_the_build_in_any_group()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        ["sync-users"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5),
                        o => o.PrunePrefix("sync-")
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "sync-extra",
                        new SchedulerTestInput(),
                        Every.Minutes(5)
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>(
                "an unnamed batch's prune is not limited to its group"
            )
            .WithMessage("*'sync-'*'sync-extra'*");
    }

    [Test]
    public void A_schedule_with_a_batchs_prefix_in_another_group_builds_when_the_batch_is_named()
    {
        var act = () =>
            Build(scheduler =>
                scheduler
                    .ScheduleMany<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                        "sync",
                        ["orders"],
                        id => (id, new SchedulerTestInput { Value = id }),
                        Every.Minutes(5)
                    )
                    .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                        "sync-extra",
                        new SchedulerTestInput(),
                        Every.Minutes(5),
                        o => o.Group("elsewhere")
                    )
            );

        act.Should().NotThrow("a named batch prunes only within its own group");
    }

    private static void Build(
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
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
                    return configure(scheduler);
                })
        );
    }
}
