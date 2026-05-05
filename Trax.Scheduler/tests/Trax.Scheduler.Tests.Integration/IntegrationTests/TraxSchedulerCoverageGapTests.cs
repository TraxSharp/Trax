using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

[TestFixture]
public class TraxSchedulerCoverageGapTests : TestSetup
{
    private ITraxScheduler _scheduler = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
    }

    #region TriggerAsync(externalId, delay) — delayed work-queue insertion

    [Test]
    public async Task TriggerAsyncWithDelay_QueuesWorkItemAtFutureScheduledAt()
    {
        // Arrange
        var manifest = await CreateScheduledManifest("delayed-trigger-1");
        var delay = TimeSpan.FromMinutes(15);
        var beforeTrigger = DateTime.UtcNow;

        // Act
        await _scheduler.TriggerAsync(manifest.ExternalId, delay);

        // Assert
        DataContext.Reset();
        var queued = await DataContext
            .WorkQueues.AsNoTracking()
            .Where(w => w.ManifestId == manifest.Id)
            .ToListAsync();

        queued.Should().HaveCount(1);
        queued[0].ScheduledAt.Should().BeAfter(beforeTrigger.AddMinutes(14));
        queued[0].ScheduledAt.Should().BeBefore(beforeTrigger.AddMinutes(16));
        queued[0].TrainName.Should().Be(manifest.Name);
    }

    [Test]
    public async Task TriggerAsyncWithDelay_NonexistentExternalId_Throws()
    {
        var act = () => _scheduler.TriggerAsync("does-not-exist-trigger", TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region ScheduleOnceAsync<TTrain, TInput, TOutput>(externalId, ...)

    [Test]
    public async Task ScheduleOnceAsync_Typed_WithExternalId_PersistsManifest()
    {
        var externalId = $"once-{Guid.NewGuid():N}";
        var input = new SchedulerTestInput { Value = "once-typed" };
        var delay = TimeSpan.FromHours(1);

        var manifest = await _scheduler.ScheduleOnceAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(externalId, input, delay);

        manifest.ExternalId.Should().Be(externalId);
        manifest.ScheduleType.Should().Be(ScheduleType.Once);
        manifest.ScheduledAt.Should().NotBeNull();
        manifest.ScheduledAt!.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(55));

        DataContext.Reset();
        var persisted = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == externalId);
        persisted.Name.Should().Be(typeof(ISchedulerTestTrain).FullName);
    }

    [Test]
    public async Task ScheduleOnceAsync_Typed_AutoExternalId_GeneratesUniqueIds()
    {
        var input = new SchedulerTestInput { Value = "auto-id" };

        var first = await _scheduler.ScheduleOnceAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(input, TimeSpan.FromMinutes(5));
        var second = await _scheduler.ScheduleOnceAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            Unit
        >(input, TimeSpan.FromMinutes(5));

        first.ExternalId.Should().StartWith("once-");
        second.ExternalId.Should().StartWith("once-");
        first.ExternalId.Should().NotBe(second.ExternalId);
    }

    #endregion

    #region Helper

    private async Task<Manifest> CreateScheduledManifest(string externalId)
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "trigger" },
            }
        );
        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return manifest;
    }

    #endregion
}
