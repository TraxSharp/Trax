using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.StateMachine.E2E.Fakes;

/// <summary>The input of <see cref="WizardTrigger.Describe"/>.</summary>
public sealed record WizardDetailsInput
{
    [MinLength(1)]
    public string Title { get; init; } = "";

    public int Pages { get; init; }

    /// <summary>Named differently on the wire, so validation must read it under its JSON name.</summary>
    [JsonPropertyName("note_text")]
    [MaxLength(10)]
    public string? Note { get; init; }

    /// <summary>Never recorded anywhere a run's input is copied.</summary>
    [TraxSensitive]
    public string? Secret { get; init; }
}
