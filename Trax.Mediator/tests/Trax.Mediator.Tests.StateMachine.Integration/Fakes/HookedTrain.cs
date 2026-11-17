using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Has an OnQueue hook and defers promotion, both of which commit outside the caller's transaction.</summary>
public class HookedTrain : ServiceTrain<HookedInput, JobOutput>, IHookedTrain
{
    protected override bool DeferQueuePromotion => true;

    protected override Task OnQueue(Metadata metadata, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<HookedStep>().Resolve();
}
