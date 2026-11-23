using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

// A query train answering with an entity graph that refers back to itself, as an EF model with navigations both ways
// does: the order's customer lists the customer's orders.

public record FindOrderInput
{
    public int Id { get; init; }
}
