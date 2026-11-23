using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>The question <see cref="InvokedDecidingTrain"/> asks.</summary>
[Asks("Which lane does this invoked run take?")]
public enum InvokedLane
{
    Express,
    Ground,
}
