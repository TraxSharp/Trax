using System.Collections.Concurrent;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Two trains may take the same input type. A scheduled run names its train, and dispatch runs
/// that train, not whichever train the registry keeps for the input type.
/// </summary>
[TestFixture]
public class SharedInputTypeDispatchTests
{
    /// <summary>The input both trains take.</summary>
    public record SharedDispatchInput : IManifestProperties
    {
        public string Tag { get; set; } = string.Empty;
    }

    public interface IFirstSharedInputTrain : IServiceTrain<SharedDispatchInput, Unit>;

    public interface ISecondSharedInputTrain : IServiceTrain<SharedDispatchInput, Unit>;

    /// <summary>Takes the shared input and is never scanned: nothing implements it.</summary>
    public interface IUnregisteredSharedInputTrain : IServiceTrain<SharedDispatchInput, Unit>;

    /// <summary>Which train ran for which tag. Tags are unique per test.</summary>
    public static ConcurrentDictionary<string, string> Ran { get; } = new();

    public class FirstSharedInputTrain
        : ServiceTrain<SharedDispatchInput, Unit>,
            IFirstSharedInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<RecordRun<IFirstSharedInputTrain>>().Resolve();
    }

    public class SecondSharedInputTrain
        : ServiceTrain<SharedDispatchInput, Unit>,
            ISecondSharedInputTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<RecordRun<ISecondSharedInputTrain>>().Resolve();
    }

    public sealed class RecordRun<TTrain> : Junction<SharedDispatchInput, Unit>
    {
        public override Task<Unit> Run(SharedDispatchInput input)
        {
            Ran[input.Tag] = typeof(TTrain).FullName!;
            return Task.FromResult(Unit.Default);
        }
    }

    [Test]
    public async Task Each_scheduled_run_executes_the_train_it_names()
    {
        var firstTag = $"first-{Guid.NewGuid():N}";
        var secondTag = $"second-{Guid.NewGuid():N}";

        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
        {
            s.Schedule<IFirstSharedInputTrain>(
                "shared-first",
                new SharedDispatchInput { Tag = firstTag },
                Every.Minutes(5)
            );
            s.Schedule<ISecondSharedInputTrain>(
                "shared-second",
                new SharedDispatchInput { Tag = secondTag },
                Every.Minutes(5)
            );
        });
        await fx.MaterializePendingManifestsAsync();

        await fx.Scheduler.TriggerAsync("shared-first");
        await fx.Scheduler.TriggerAsync("shared-second");
        await fx.RunJobDispatcherAsync();

        Ran.Should()
            .ContainKey(firstTag)
            .WhoseValue.Should()
            .Be(typeof(IFirstSharedInputTrain).FullName);
        Ran.Should()
            .ContainKey(secondTag)
            .WhoseValue.Should()
            .Be(
                typeof(ISecondSharedInputTrain).FullName,
                "the run names the second train, so the second train runs"
            );

        var names = new[]
        {
            typeof(IFirstSharedInputTrain).FullName!,
            typeof(ISecondSharedInputTrain).FullName!,
        };
        var runs = await fx
            .DataContext.Metadatas.AsNoTracking()
            .Where(m => names.Contains(m.Name))
            .ToListAsync();
        runs.Should().HaveCount(2);
        runs.Should().OnlyContain(m => m.TrainState == TrainState.Completed);
    }

    [Test]
    public async Task Scheduling_a_train_that_is_not_registered_is_refused_even_when_its_input_type_is()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        var act = () =>
            fx.Scheduler.ScheduleAsync<IUnregisteredSharedInputTrain, SharedDispatchInput, Unit>(
                "shared-unregistered",
                new SharedDispatchInput { Tag = "never" },
                Every.Minutes(5)
            );

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain(typeof(IUnregisteredSharedInputTrain).FullName!)
            .And.Contain("ScanAssemblies");
    }

    [Test]
    public async Task A_scheduler_built_with_the_compatibility_constructor_checks_only_the_input_type_and_keeps_the_built_in_defaults()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s => s.DefaultMaxRetries(7));
        var scheduler = new TraxScheduler(
            fx.Services.GetRequiredService<IDataContextProviderFactory>(),
            fx.Services.GetRequiredService<ITrainRegistry>(),
            fx.Services.GetRequiredService<ICancellationRegistry>(),
            NullLogger<TraxScheduler>.Instance
        );

        var manifest = await scheduler.ScheduleAsync<
            IUnregisteredSharedInputTrain,
            SharedDispatchInput,
            Unit
        >("shared-compat", new SharedDispatchInput { Tag = "never" }, Every.Minutes(5));

        manifest
            .Name.Should()
            .Be(
                typeof(IUnregisteredSharedInputTrain).FullName,
                "without the discovery service only the input type can be checked, and a train "
                    + "takes it"
            );
        manifest
            .MaxRetries.Should()
            .Be(
                new ManifestOptions().MaxRetries,
                "without the configuration the built-in default applies, not DefaultMaxRetries(7)"
            );

        // No change signal either: a delayed trigger and a group trigger still queue.
        await scheduler.TriggerAsync("shared-compat", TimeSpan.FromMinutes(10));
        (
            await fx
                .DataContext.WorkQueues.AsNoTracking()
                .CountAsync(w => w.ManifestId == manifest.Id && w.ScheduledAt != null)
        )
            .Should()
            .Be(1);

        var other = await scheduler.ScheduleAsync<
            IUnregisteredSharedInputTrain,
            SharedDispatchInput,
            Unit
        >("shared-compat-other", new SharedDispatchInput { Tag = "never" }, Every.Minutes(5));
        (await scheduler.TriggerGroupAsync(other.ManifestGroupId)).Should().Be(1);
    }
}
