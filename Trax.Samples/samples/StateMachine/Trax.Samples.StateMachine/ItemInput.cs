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

/// <summary>The input of <see cref="CheckoutTrigger.AddItem"/>: the item to put in the cart.</summary>
public sealed record ItemInput
{
    public string Item { get; init; } = "";
}
