using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Which execution paths can reach the dormant dependent machinery.
///
/// <para>Activation is anchored on a parent manifest: <c>RunScheduledTrainJunction</c> seeds the
/// context from <c>metadata.ManifestId</c> before the train runs, and <c>ActivateAsync</c> refuses
/// a dependent that does not declare that manifest as its parent. A train submitted through
/// <c>ITrainExecutionService</c>, which is what a GraphQL mutation does, has no manifest, so
/// nothing seeds the context and every activation from it is skipped.</para>
///
/// <para>The pair matters more than either test alone: the scheduled case is what proves the
/// harness can observe an activation at all, so the ad-hoc case failing to produce one is a fact
/// about the code rather than about the test.</para>
/// </summary>
[TestFixture]
public class DormantDependentReachabilityTests
{
    private const string Anchor = "reachability-anchor";
    private const string Dormant = "reachability-dormant";

    [Test]
    public async Task ScheduledRun_CanActivateItsDormantDependent()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();
        await fx.Scheduler.TriggerAsync(Anchor);

        await fx.RunJobDispatcherAsync();

        (await DormantEntryCount(fx))
            .Should()
            .Be(1, "the scheduled path seeds the parent manifest before the train runs");
    }

    [Test]
    public async Task AdHocRun_CannotActivateTheSameDormantDependent()
    {
        await using var fx = await CreateAsync();
        await fx.MaterializePendingManifestsAsync();

        var execution = fx.Services.GetRequiredService<ITrainExecutionService>();
        await execution.RunAsync(
            typeof(IActivatingTrain).FullName!,
            """{"value":"ad-hoc"}""",
            ct: default
        );

        (await DormantEntryCount(fx))
            .Should()
            .Be(
                0,
                "nothing seeds the parent manifest for a train submitted outside the scheduler, "
                    + "so the activation is skipped with a warning and the downstream work is lost"
            );
    }

    private static Task<SchedulerE2EFixture> CreateAsync() =>
        SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<IActivatingTrain>(
                    Anchor,
                    new ActivatingInput { Value = "seed" },
                    Every.Minutes(5)
                )
                .Include<ISchedulerTestTrain>(Dormant, new SchedulerTestInput(), o => o.Dormant())
        );

    private static async Task<int> DormantEntryCount(SchedulerE2EFixture fx)
    {
        fx.DataContext.Reset();

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == Dormant);

        return await fx
            .DataContext.WorkQueues.AsNoTracking()
            .CountAsync(w => w.ManifestId == manifest.Id);
    }

    /// <summary>A train that activates a dormant dependent from inside its own run.</summary>
    public class ActivatingTrain : ServiceTrain<ActivatingInput, Unit>, IActivatingTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Chain<ActivateTheDormantDependent>().Resolve();
    }

    /// <summary>Activates the dormant dependent, which is the call under test.</summary>
    public class ActivateTheDormantDependent(IDormantDependentContext dormants)
        : Junction<ActivatingInput, Unit>
    {
        public override async Task<Unit> Run(ActivatingInput input)
        {
            await dormants.ActivateAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                Dormant,
                new SchedulerTestInput { Value = input.Value }
            );

            return Unit.Default;
        }
    }

    public record ActivatingInput : IManifestProperties
    {
        public string Value { get; set; } = string.Empty;
    }

    public interface IActivatingTrain : IServiceTrain<ActivatingInput, Unit> { }
}
