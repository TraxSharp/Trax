using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

public sealed record FrameworkGenerics
{
    public IDictionary<string, PairedPriority> Pairs { get; init; } =
        new Dictionary<string, PairedPriority>(StringComparer.Ordinal);
    public Tuple<string, TupledPriority>? Tuple { get; init; }
    public (int, ValueTupledPriority) ValueTuple { get; init; }
}
