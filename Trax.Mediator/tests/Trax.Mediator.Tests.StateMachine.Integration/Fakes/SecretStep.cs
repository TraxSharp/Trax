using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class SecretStep : EffectJunction<SecretInput, SecretOutput>
{
    public override Task<SecretOutput> Run(SecretInput input) =>
        Task.FromResult(new SecretOutput());
}
