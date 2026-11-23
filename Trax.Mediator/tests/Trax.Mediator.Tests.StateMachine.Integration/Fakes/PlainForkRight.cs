using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class PlainForkRight : Junction<ForkInput, RightOut>
{
    public override Task<RightOut> Run(ForkInput input) => Task.FromResult(new RightOut("r"));
}
