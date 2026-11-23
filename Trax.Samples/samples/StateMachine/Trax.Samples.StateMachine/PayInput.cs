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

/// <summary>
/// The input <see cref="CheckoutTrigger.Pay"/> is fired with: the charge's receipt. <c>Pay</c> is effect-bound, so
/// the server fires it after the charge ran, with the receipt the charge returned; no client sends it.
/// </summary>
public sealed record PayInput
{
    public string Receipt { get; init; } = "";
}
