using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>ReplayDecisionsOnRetry</c> reaches the manifest through every way a manifest is scheduled,
/// and a re-seed that does not state it keeps the value the manifest has, an explicit false
/// included.
///
/// <para>Enforces <c>docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md</c>, with the re-seed rule of scheduler/0011.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md")]
public class ReplayDecisionsOnRetrySeedingTests
{
    private const string Adr =
        "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md";

    [Test]
    public async Task A_new_manifest_replays_decisions_on_retry_unless_told_otherwise()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await ScheduleAsync(fx, "default");
        await ScheduleAsync(fx, "opted-out", o => o.ReplayDecisionsOnRetry(false));

        (await Read(fx, "default")).ReplayDecisionsOnRetry.Should().BeTrue();
        (await Read(fx, "opted-out"))
            .ReplayDecisionsOnRetry.Should()
            .BeFalse($"the code states ReplayDecisionsOnRetry(false). See {Adr}");
    }

    [Test]
    public async Task A_re_seed_that_does_not_state_the_option_keeps_an_explicit_false()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await ScheduleAsync(fx, "kept", o => o.ReplayDecisionsOnRetry(false));
        await ScheduleAsync(fx, "kept");

        (await Read(fx, "kept"))
            .ReplayDecisionsOnRetry.Should()
            .BeFalse(
                $"an unstated option keeps the manifest's value. See {Adr} and scheduler/0011"
            );

        await ScheduleAsync(fx, "kept", o => o.ReplayDecisionsOnRetry(true));

        (await Read(fx, "kept"))
            .ReplayDecisionsOnRetry.Should()
            .BeTrue("a stated option is written on every seed");
    }

    [Test]
    public async Task A_batch_item_dependent_and_one_off_manifest_each_take_the_option()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await fx.Scheduler.ScheduleManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
            ["batch-a", "batch-b"],
            id => (id, new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            configureEach: (id, opts) => opts.ReplayDecisionsOnRetry = id != "batch-a"
        );
        await ScheduleAsync(fx, "parent");
        await fx.Scheduler.ScheduleDependentAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "dependent",
            new SchedulerTestInput { Value = "dependent" },
            "parent",
            o => o.ReplayDecisionsOnRetry(false)
        );
        await fx.Scheduler.ScheduleOnceAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "once",
            new SchedulerTestInput { Value = "once" },
            TimeSpan.FromHours(1),
            o => o.ReplayDecisionsOnRetry(false)
        );

        (await Read(fx, "batch-a")).ReplayDecisionsOnRetry.Should().BeFalse(Adr);
        (await Read(fx, "batch-b")).ReplayDecisionsOnRetry.Should().BeTrue(Adr);
        (await Read(fx, "dependent")).ReplayDecisionsOnRetry.Should().BeFalse(Adr);
        (await Read(fx, "once")).ReplayDecisionsOnRetry.Should().BeFalse(Adr);

        // Re-seeded without the option, each keeps its false.
        await fx.Scheduler.ScheduleDependentAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "dependent",
            new SchedulerTestInput { Value = "dependent" },
            "parent"
        );
        await fx.Scheduler.ScheduleOnceAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            "once",
            new SchedulerTestInput { Value = "once" },
            TimeSpan.FromHours(1)
        );

        (await Read(fx, "dependent")).ReplayDecisionsOnRetry.Should().BeFalse(Adr);
        (await Read(fx, "once")).ReplayDecisionsOnRetry.Should().BeFalse(Adr);
    }

    [Test]
    public async Task Reading_the_option_back_in_configureEach_does_not_state_it()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await ScheduleAsync(fx, "batch-kept", o => o.ReplayDecisionsOnRetry(false));

        // Read-modify-write of an unstated option: it stays unstated, so the false stands.
        await fx.Scheduler.ScheduleManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
            ["batch-kept"],
            id => (id, new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            configureEach: (_, opts) => opts.ReplayDecisionsOnRetry = opts.ReplayDecisionsOnRetry
        );

        (await Read(fx, "batch-kept"))
            .ReplayDecisionsOnRetry.Should()
            .BeFalse($"an option read back unstated is written back unstated. See {Adr}");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_re_seed_that_turns_replay_off_clears_the_queued_retrys_link(bool stateFalse)
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        var manifest = await ScheduleAsync(fx, "queued-retry");
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                ReplayDecisionsOf = 4242,
            }
        );
        fx.DataContext.WorkQueues.Add(entry);
        await fx.DataContext.SaveChanges(CancellationToken.None);

        if (stateFalse)
            await ScheduleAsync(fx, "queued-retry", o => o.ReplayDecisionsOnRetry(false));
        else
            await ScheduleAsync(fx, "queued-retry");

        fx.DataContext.Reset();
        var queued = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .SingleAsync(q => q.Id == entry.Id);
        if (stateFalse)
            queued
                .ReplayDecisionsOf.Should()
                .BeNull(
                    $"a manifest that stops replaying has its queued retry ask afresh. See {Adr}"
                );
        else
            queued
                .ReplayDecisionsOf.Should()
                .Be(4242, "a re-seed that states nothing changes nothing");
    }

    [Test]
    public async Task The_runtime_setter_refuses_an_empty_list_and_counts_only_changes()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });
        var manifest = await ScheduleAsync(fx, "runtime");
        var ops = fx.Services.GetRequiredService<IOperationsService>();

        (await ops.SetManifestsReplayDecisionsOnRetryAsync([], false, CancellationToken.None))
            .Success.Should()
            .BeFalse();

        (
            await ops.SetManifestsReplayDecisionsOnRetryAsync(
                [manifest.Id],
                true,
                CancellationToken.None
            )
        )
            .Count.Should()
            .Be(0, "the manifest already replays");
        (
            await ops.SetManifestsReplayDecisionsOnRetryAsync(
                [manifest.Id],
                false,
                CancellationToken.None
            )
        )
            .Count.Should()
            .Be(1);

        fx.DataContext.Reset();
        (await Read(fx, "runtime")).ReplayDecisionsOnRetry.Should().BeFalse(Adr);
    }

    private static Task<Manifest> ScheduleAsync(
        SchedulerE2EFixture fx,
        string externalId,
        Action<Configuration.ScheduleOptions>? options = null
    ) =>
        fx.Scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            externalId,
            new SchedulerTestInput { Value = externalId },
            Every.Minutes(5),
            options
        );

    private static Task<Manifest> Read(SchedulerE2EFixture fx, string externalId) =>
        fx.DataContext.Manifests.AsNoTracking().FirstAsync(m => m.ExternalId == externalId);
}
