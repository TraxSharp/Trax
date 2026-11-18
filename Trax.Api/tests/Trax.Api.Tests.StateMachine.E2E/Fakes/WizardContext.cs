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

/// <summary>The wizard's context, one shape in every state.</summary>
public sealed record WizardContext
{
    public string? Title { get; init; }

    public int? Pages { get; init; }

    public string? Note { get; init; }

    /// <summary>The receipt the payment effect returned.</summary>
    public string? Receipt { get; init; }

    /// <summary>What the build made: a pointer, never the thing.</summary>
    public string? Artifact { get; init; }
}
