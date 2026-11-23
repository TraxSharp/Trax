using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

public record CustomerOutput
{
    public string Name { get; init; } = "";
    public OrderOutput[] Orders { get; init; } = [];
}
