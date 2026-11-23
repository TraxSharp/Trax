using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

internal sealed class EchoJunction : Junction<EchoInput, EchoOutput>
{
    public override Task<EchoOutput> Run(EchoInput input) =>
        Task.FromResult(new EchoOutput { Reply = $"echo: {input.Message}" });
}
