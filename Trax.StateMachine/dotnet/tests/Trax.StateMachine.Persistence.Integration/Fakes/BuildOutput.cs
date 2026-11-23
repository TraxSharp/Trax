using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>A pointer to what the build produced, never the artifact itself.</summary>
public sealed record BuildOutput
{
    public string Artifact { get; init; } = "";
}
