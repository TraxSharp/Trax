using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

[TraxAllowAnonymous]
[TraxQuery(Description = "Seven types that each list the six others.")]
public class ReadMesh : ServiceTrain<ReadMeshInput, Mesh0Output>, IReadMesh
{
    protected override Task<Either<Exception, Mesh0Output>> Junctions() =>
        Chain<ReadMeshJunction>().Resolve();
}
