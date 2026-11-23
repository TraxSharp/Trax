using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class ForkLeft : EffectJunction<ForkInput, LeftOut>
{
    public override Task<LeftOut> Run(ForkInput input) => Task.FromResult(new LeftOut("l"));
}
