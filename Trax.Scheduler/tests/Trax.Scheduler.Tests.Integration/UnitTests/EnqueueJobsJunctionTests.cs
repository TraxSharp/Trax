using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Trains.ManifestManager.Junctions;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Direct unit tests for <see cref="EnqueueJobsJunction"/>'s Run method against an
/// InMemory data context. Exercises happy-path enqueueing (multiple manifests),
/// per-manifest exception isolation, and the "no jobs enqueued" debug path.
/// </summary>
[TestFixture]
public class EnqueueJobsJunctionTests
{
    private static (IDataContext context, IJobSubmitter submitter) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IDataContextProviderFactory>();
        var context = (IDataContext)factory.Create();
        var submitter = Substitute.For<IJobSubmitter>();
        submitter
            .EnqueueAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid().ToString("N"));
        return (context, submitter);
    }

    private static Manifest BuildManifest(string id) =>
        Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = id },
            }
        );

    [Test]
    public async Task Run_MultipleManifests_PersistsMetadataAndEnqueuesEach()
    {
        var (context, submitter) = Build();
        var junction = new EnqueueJobsJunction(
            context,
            submitter,
            NullLogger<EnqueueJobsJunction>.Instance
        );

        var manifests = new List<Manifest> { BuildManifest("a"), BuildManifest("b") };
        // Persist manifests so they have ids.
        foreach (var m in manifests)
            await context.Track(m);
        await context.SaveChanges(CancellationToken.None);

        await junction.Run(manifests);

        await submitter.Received(2).EnqueueAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());

        var metadatas = await context
            .Metadatas.AsNoTracking()
            .Where(md => manifests.Select(m => m.Id).Contains(md.ManifestId!.Value))
            .ToListAsync();
        metadatas.Should().HaveCount(2);
    }

    [Test]
    public async Task Run_SubmitterThrows_OtherManifestsStillProcessed()
    {
        var (context, _) = Build();

        var submitter = Substitute.For<IJobSubmitter>();
        var calls = 0;
        submitter
            .EnqueueAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                    throw new InvalidOperationException("first one fails");
                return "ok";
            });

        var junction = new EnqueueJobsJunction(
            context,
            submitter,
            NullLogger<EnqueueJobsJunction>.Instance
        );

        var manifests = new List<Manifest> { BuildManifest("x"), BuildManifest("y") };
        foreach (var m in manifests)
            await context.Track(m);
        await context.SaveChanges(CancellationToken.None);

        await junction.Run(manifests);

        // Both calls hit (the per-manifest try/catch keeps the loop alive).
        calls.Should().Be(2);
    }

    [Test]
    public async Task Run_EmptyList_DoesNothing()
    {
        var (context, submitter) = Build();
        var junction = new EnqueueJobsJunction(
            context,
            submitter,
            NullLogger<EnqueueJobsJunction>.Instance
        );

        await junction.Run([]);

        await submitter.DidNotReceive().EnqueueAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
