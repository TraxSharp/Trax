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

public enum WizardState
{
    /// <summary>The wizard's first step: what to make and how many pages.</summary>
    Naming,

    /// <summary>The order is described and waits for its payment.</summary>
    Paying,

    /// <summary>Paid; an approver lets the build start. Held too long, it expires.</summary>
    Approving,

    /// <summary>The build runs: <see cref="IBuildWizardTrain"/>.</summary>
    Building,

    /// <summary>The build finished; the context points at what it made.</summary>
    Built,

    /// <summary>The build failed.</summary>
    BuildFailed,

    /// <summary>The build's run was cancelled.</summary>
    BuildCancelled,

    /// <summary>Nobody approved in time.</summary>
    Expired,

    /// <summary>The build was stopped by its owner; an approver may start it again.</summary>
    Held,
}
