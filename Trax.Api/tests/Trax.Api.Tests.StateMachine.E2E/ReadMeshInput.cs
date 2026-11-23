using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

// A query train answering seven types that each list all six others, as an EF model whose entities navigate to each
// other does. Selecting everything such an answer reaches grows with the number of paths through it (7,828 fields for
// these seven), so no host may try: every host in this suite exposes it, and a client document reaches it.

public record ReadMeshInput
{
    public string Name { get; init; } = "";
}
