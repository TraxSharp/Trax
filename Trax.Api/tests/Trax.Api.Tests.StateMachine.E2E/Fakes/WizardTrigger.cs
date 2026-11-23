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

public enum WizardTrigger
{
    /// <summary>Describe what to make; carries <see cref="WizardDetailsInput"/>.</summary>
    Describe,

    /// <summary>Back from the payment to the description.</summary>
    Back,

    /// <summary>Pay: runs the irreversible <see cref="IOrderCharge"/> exactly once. Needs the Payer role.</summary>
    Pay,

    /// <summary>Approve the paid order, which starts the build. Needs the Approver role.</summary>
    Approve,

    /// <summary>Stop the running build.</summary>
    CancelBuild,

    /// <summary>Build again with the same choices: a new run. Needs the Approver role.</summary>
    Rebuild,

    /// <summary>Start over from the description.</summary>
    Edit,
}
