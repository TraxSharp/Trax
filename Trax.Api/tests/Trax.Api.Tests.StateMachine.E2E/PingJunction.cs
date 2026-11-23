using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

public class PingJunction : Junction<PingInput, PingOutput>
{
    public override Task<PingOutput> Run(PingInput input) =>
        Task.FromResult(new PingOutput { Value = input.Value });
}
