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

public sealed class BuildWizardStep : EffectJunction<BuildWizardInput, WizardArtifact>
{
    public override Task<WizardArtifact> Run(BuildWizardInput input) =>
        Task.FromResult(
            new WizardArtifact
            {
                Artifact = string.Create(
                    CultureInfo.InvariantCulture,
                    $"artifact:{input.Title}:{input.Pages}"
                ),
            }
        );
}
