using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public class SecretTrain : ServiceTrain<SecretInput, SecretOutput>, ISecretTrain
{
    protected override Task<Either<Exception, SecretOutput>> Junctions() =>
        Chain<SecretStep>().Resolve();
}
