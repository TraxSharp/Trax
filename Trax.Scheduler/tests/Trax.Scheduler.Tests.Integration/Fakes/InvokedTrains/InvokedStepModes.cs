using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// What an <see cref="IInvokedStepTrain"/> run does, named by its input: <c>ok</c> succeeds; <c>chain</c> succeeds
/// with an output that routes to a state invoking a run of its own; <c>unaccepted</c> succeeds with an output no
/// <c>OnDone</c> guard accepts; <c>fail</c> fails; <c>big</c> succeeds with an output past 64 KiB; <c>grow</c>
/// succeeds with an output under the cap that grows the snapshot past it; <c>gate</c> waits at
/// <see cref="InvokedStepGate"/> between its two junctions; <c>owner</c> succeeds with the
/// <see cref="InvokedRun"/>'s user key as its artifact.
/// </summary>
public static class InvokedStepModes
{
    public const string Ok = "ok";
    public const string Chain = "chain";
    public const string Unaccepted = "unaccepted";
    public const string Fail = "fail";
    public const string Big = "big";
    public const string Grow = "grow";
    public const string Gate = "gate";
    public const string Owner = "owner";

    /// <summary>Short-circuits, in <see cref="IInvokedShortCircuitTrain"/>.</summary>
    public const string Short = "short";

    /// <summary>Succeeds with an artifact that is the note reversed, so it appears in no input.</summary>
    public const string Secret = "secret";

    public static string Reversed(string note) => new(note.Reverse().ToArray());
}
