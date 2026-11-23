using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

internal sealed class NotifyJunction : Junction<NotifyInput, NotifyOutput>
{
    public override Task<NotifyOutput> Run(NotifyInput input) =>
        Task.FromResult(new NotifyOutput { DeliveryId = $"{input.Topic}:{Guid.NewGuid():N}" });
}
