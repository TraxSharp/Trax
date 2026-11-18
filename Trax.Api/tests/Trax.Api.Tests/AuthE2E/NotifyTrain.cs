using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

[TraxMutation(Namespace = "audit", Description = "Notifies a topic.")]
[TraxAllowAnonymous]
public class NotifyTrain : ServiceTrain<NotifyInput, NotifyOutput>, INotifyTrain
{
    protected override Task<Either<Exception, NotifyOutput>> Junctions() =>
        Chain<NotifyJunction>().Resolve();
}
