using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Exercises the ScheduleFunc lambda bodies inside SchedulerConfigurationBuilder by
/// invoking each pending manifest's closure against a substitute scheduler and
/// verifying it routes to the expected ITraxScheduler method.
/// </summary>
[TestFixture]
public class SchedulerBuilderLambdaInvocationTests
{
    private static SchedulerConfiguration ResolveConfiguration(
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

    [Test]
    public async Task Schedule_LambdaInvocation_RoutesToScheduleAsync()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<SchedulerTestTrain, SchedulerTestInput, Unit>(
                "ext-sched",
                new SchedulerTestInput(),
                Every.Minutes(5)
            )
        );

        var scheduler = Substitute.For<ITraxScheduler>();
        var dummyManifest = (Manifest)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Manifest));
        scheduler
            .ScheduleAsync<SchedulerTestTrain, SchedulerTestInput, Unit>(
                Arg.Any<string>(),
                Arg.Any<SchedulerTestInput>(),
                Arg.Any<Trax.Scheduler.Services.Scheduling.Schedule>(),
                Arg.Any<Action<ScheduleOptions>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(dummyManifest);

        var pending = config.PendingManifests.Single(m => m.ExternalId == "ext-sched");
        await pending.ScheduleFunc(scheduler, CancellationToken.None);

        await scheduler
            .Received(1)
            .ScheduleAsync<SchedulerTestTrain, SchedulerTestInput, Unit>(
                "ext-sched",
                Arg.Any<SchedulerTestInput>(),
                Arg.Any<Trax.Scheduler.Services.Scheduling.Schedule>(),
                Arg.Any<Action<ScheduleOptions>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ScheduleOnce_LambdaInvocation_RoutesToScheduleOnceAsync()
    {
        var config = ResolveConfiguration(s =>
            s.ScheduleOnce<SchedulerTestTrain, SchedulerTestInput, Unit>(
                "once-ext",
                new SchedulerTestInput(),
                TimeSpan.FromMinutes(2)
            )
        );

        var scheduler = Substitute.For<ITraxScheduler>();
        var dummyManifest = (Manifest)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Manifest));
        scheduler
            .ScheduleOnceAsync<SchedulerTestTrain, SchedulerTestInput, Unit>(
                Arg.Any<string>(),
                Arg.Any<SchedulerTestInput>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<Action<ScheduleOptions>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(dummyManifest);

        var pending = config.PendingManifests.Single(m => m.ExternalId == "once-ext");
        await pending.ScheduleFunc(scheduler, CancellationToken.None);

        await scheduler
            .Received(1)
            .ScheduleOnceAsync<SchedulerTestTrain, SchedulerTestInput, Unit>(
                "once-ext",
                Arg.Any<SchedulerTestInput>(),
                TimeSpan.FromMinutes(2),
                Arg.Any<Action<ScheduleOptions>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Include_LambdaInvocation_RoutesToScheduleDependentAsync()
    {
        var config = ResolveConfiguration(s =>
            s.Schedule<SchedulerTestTrain, SchedulerTestInput, Unit>(
                    "root-x",
                    new SchedulerTestInput(),
                    Every.Minutes(5)
                )
                .Include<SchedulerTestTrain, SchedulerTestInput, Unit>(
                    "branch-x",
                    new SchedulerTestInput()
                )
        );

        var scheduler = Substitute.For<ITraxScheduler>();
        var dummyManifest = (Manifest)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Manifest));
        scheduler
            .ScheduleDependentAsync<SchedulerTestTrain, SchedulerTestInput, Unit>(
                Arg.Any<string>(),
                Arg.Any<SchedulerTestInput>(),
                Arg.Any<string>(),
                Arg.Any<Action<ScheduleOptions>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(dummyManifest);

        var pending = config.PendingManifests.Single(m => m.ExternalId == "branch-x");
        await pending.ScheduleFunc(scheduler, CancellationToken.None);

        await scheduler
            .Received(1)
            .ScheduleDependentAsync<SchedulerTestTrain, SchedulerTestInput, Unit>(
                "branch-x",
                Arg.Any<SchedulerTestInput>(),
                "root-x",
                Arg.Any<Action<ScheduleOptions>?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
