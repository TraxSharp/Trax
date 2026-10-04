using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A setting changed at runtime changes what the running scheduler does.
/// </summary>
[TestFixture]
public class SchedulerSettingsAppliedLiveTests
{
    [Test]
    public async Task Turning_AutoPurgeDeadLetters_off_stops_resolved_dead_letters_being_deleted()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "purge-target",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        var ops = fx.Services.GetRequiredService<IOperationsService>();
        var result = await ops.UpdateSchedulerConfigAsync(
            new UpdateSchedulerConfigInput(AutoPurgeDeadLetters: false),
            CancellationToken.None
        );
        result.Success.Should().BeTrue();
        ops.GetSchedulerConfig().AutoPurgeDeadLetters.Should().BeFalse();

        var manifest = await fx.DataContext.Manifests.FirstAsync(m =>
            m.ExternalId == "purge-target"
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "kept for investigation",
                RetryCount = 3,
            }
        );
        deadLetter.Acknowledge("resolved long ago");
        deadLetter.ResolvedAt = DateTime.UtcNow.AddDays(-60);
        await fx.DataContext.Track(deadLetter);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();

        await fx.RunDeadLetterCleanupAsync();

        var stillThere = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .AnyAsync(d => d.Id == deadLetter.Id);
        stillThere
            .Should()
            .BeTrue(
                "the operator turned AutoPurgeDeadLetters off, and UpdateSchedulerConfigAsync "
                    + "says a change takes effect immediately"
            );
    }

    [Test]
    public async Task AutoPurgeDeadLetters_false_in_code_keeps_resolved_dead_letters()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.AutoPurgeDeadLetters(false)
                .DeadLetterRetentionPeriod(TimeSpan.FromDays(7))
                .Schedule<ISchedulerTestTrain>(
                    "purge-off-in-code",
                    new SchedulerTestInput { Value = "x" },
                    Every.Minutes(5)
                )
        );
        await fx.MaterializePendingManifestsAsync();

        fx.Configuration.AutoPurgeDeadLetters.Should().BeFalse();
        fx.Configuration.DeadLetterRetentionPeriod.Should().Be(TimeSpan.FromDays(7));

        var manifest = await fx.DataContext.Manifests.FirstAsync(m =>
            m.ExternalId == "purge-off-in-code"
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "kept",
                RetryCount = 3,
            }
        );
        deadLetter.Acknowledge("resolved long ago");
        deadLetter.ResolvedAt = DateTime.UtcNow.AddDays(-60);
        await fx.DataContext.Track(deadLetter);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();

        await fx.RunDeadLetterCleanupAsync();

        (await fx.DataContext.DeadLetters.AsNoTracking().AnyAsync(d => d.Id == deadLetter.Id))
            .Should()
            .BeTrue();
    }
}
