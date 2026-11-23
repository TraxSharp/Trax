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

/// <summary>The input the server builds for <see cref="IBuildWizardTrain"/> from the context.</summary>
public sealed record BuildWizardInput
{
    public string Title { get; init; } = "";

    public int Pages { get; init; }
}
