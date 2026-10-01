using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A train marked <c>[TraxRemote]</c> on a scheduler with no routed submitter
/// (<c>UseRemoteWorkers</c>, <c>UseSqsWorkers</c> or <c>UseLambdaWorkers</c>) fails the build
/// rather than running on the scheduler host's own workers.
///
/// <para>Enforces docs/adr/0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md")]
public class TraxRemoteRoutingBuildTests
{
    [Test]
    public void A_TraxRemote_train_with_no_routed_submitter_fails_the_build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRemoteCoverageTrain, RemoteCoverageTrain>();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(IRemoteCoverageTrain).FullName}*")
            .WithMessage("*[TraxRemote]*")
            .WithMessage(
                "*UseRemoteWorkers*",
                "a train marked remote must not run on the scheduler host. See docs/adr/0016-a-traxremote-train-with-nowhere-to-go-fails-the-build.md."
            );
    }

    [Test]
    public void A_TraxRemote_train_with_a_routed_submitter_builds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRemoteCoverageTrain, RemoteCoverageTrain>();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.UseRemoteWorkers(o => o.BaseUrl = "http://endpoint")
                    )
            );

        act.Should().NotThrow();
    }
}
