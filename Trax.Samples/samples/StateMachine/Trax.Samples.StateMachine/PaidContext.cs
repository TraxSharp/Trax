using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.StateMachine;

/// <summary>The checkout's context in <c>Paid</c>: the same, with the charge's receipt.</summary>
public sealed record PaidContext
{
    public IReadOnlyList<string> Items { get; init; } = [];

    public string Receipt { get; init; } = "";

    public long Total { get; init; }
}
