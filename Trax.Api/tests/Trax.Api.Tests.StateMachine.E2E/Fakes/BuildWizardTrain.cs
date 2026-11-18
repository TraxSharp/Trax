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

/// <summary>The train <see cref="WizardState.Building"/> invokes: effect junctions only, so a cancel stops it.</summary>
public class BuildWizardTrain : ServiceTrain<BuildWizardInput, WizardArtifact>, IBuildWizardTrain
{
    protected override Task<Either<Exception, WizardArtifact>> Junctions() =>
        Chain<BuildWizardStep>().Resolve();
}
