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

/// <summary>The turnstile's context in <c>Unlocked</c>: the coin that paid. <c>Locked</c> holds none.</summary>
public sealed record UnlockedContext
{
    [MinLength(1)]
    public string PaidWith { get; init; } = "";
}
