using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// A decision that cannot be written fails its step, and a replay a host cannot honour fails its
/// run, rather than either going ahead without the record.
/// </summary>
public class DecisionRecordingFailureTests
{
    private static readonly DeciderSlot Decider = new();

    [Test]
    public async Task A_decision_that_cannot_be_written_fails_its_step_as_transient()
    {
        var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddSingleton(_ => new DecisionJournal(new DownFactory()))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory().AddDecisionRecording())
            )
            .BuildServiceProvider();
        await using var providerDisposal = provider.ConfigureAwait(false);
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));
        Ship.Ran = 0;

        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (RouteOrder)scope.ServiceProvider.GetRequiredService<IRouteOrder>();

        var run = () => train.Run(new Order("down", 20m), CancellationToken.None);

        (await run.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Be("the database is down");
        decider.Requests.Should().ContainSingle();
        Ship.Ran.Should().Be(0, "a decision that was not recorded is not acted on");
        train.Metadata.NotNull().FailureClass.Should().Be(FailureClass.Transient);
    }

    [Test]
    public async Task A_replay_on_a_host_that_records_no_decisions_fails_the_run_as_permanent()
    {
        var provider = DecisionTrains
            .Register(new ServiceCollection(), Decider)
            .AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()))
            .BuildServiceProvider();
        await using var providerDisposal = provider.ConfigureAwait(false);
        var decider = Decider.Use(new ScriptedDecider().Choose(Fulfilment.Standard));

        var scope = provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train = (RouteOrder)scope.ServiceProvider.GetRequiredService<IRouteOrder>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IRouteOrder).FullName.NotNull(),
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new Order("unconfigured", 20m),
                ReplayDecisionsOf = 42,
            },
            TestTime.Clock
        );

        var run = () => train.Run(new Order("unconfigured", 20m), metadata, CancellationToken.None);

        (await run.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("does not record decisions (AddDecisionRecording)");
        train.Metadata.NotNull().DecisionsRecorded.Should().BeFalse();
        decider.Requests.Should().BeEmpty();
        train.Metadata.NotNull().TrainState.Should().Be(TrainState.Failed);
        train.Metadata.FailureClass.Should().Be(FailureClass.Permanent);
    }

    /// <summary>A store that cannot be reached.</summary>
    private sealed class DownFactory : IDataContextProviderFactory
    {
        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the database is down");

        public IEffectProvider Create() =>
            throw new InvalidOperationException("the database is down");
    }
}
