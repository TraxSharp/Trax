using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

public class ReadMeshJunction : Junction<ReadMeshInput, Mesh0Output>
{
    public override Task<Mesh0Output> Run(ReadMeshInput input) =>
        Task.FromResult(
            new Mesh0Output
            {
                Name = input.Name,
                Mesh1s =
                [
                    new Mesh1Output { Name = "one", Mesh2s = [new Mesh2Output { Name = "two" }] },
                ],
            }
        );
}
