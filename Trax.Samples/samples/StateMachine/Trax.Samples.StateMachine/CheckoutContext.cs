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

/// <summary>The checkout's context in <c>Cart</c> and <c>Review</c>: the items, their total in cents, and no receipt yet.</summary>
public sealed record CheckoutContext
{
    public IReadOnlyList<string> Items { get; init; } = [];

    public string? Receipt { get; init; }

    /// <summary>The total in cents, <see cref="CheckoutMachine.UnitPriceCents"/> per item, which the server computes.</summary>
    public long Total { get; init; }
}
