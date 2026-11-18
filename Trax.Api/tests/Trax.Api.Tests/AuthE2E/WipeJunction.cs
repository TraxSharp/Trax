using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

internal sealed class WipeJunction : Junction<WipeInput, WipeOutput>
{
    public override Task<WipeOutput> Run(WipeInput input) =>
        Task.FromResult(new WipeOutput { Acknowledged = $"wiped:{input.Target}" });
}
