using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

public class EchoJunction : Junction<EchoInput, EchoOutput>
{
    public override Task<EchoOutput> Run(EchoInput input) =>
        Task.FromResult(new EchoOutput(input.Message));
}
