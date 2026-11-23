using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>The input of <see cref="PartsTrigger.Lookup"/>, which a GraphQL query train also takes.</summary>
public sealed record CatalogLookupInput
{
    public string Sku { get; init; } = "";
}
