using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public sealed class InvokedStepFinish(IServiceProvider services)
    : EffectJunction<InvokedStepGated, InvokedStepOutput>
{
    public override Task<InvokedStepOutput> Run(InvokedStepGated input) =>
        input.Mode switch
        {
            InvokedStepModes.Owner => Task.FromResult(
                new InvokedStepOutput
                {
                    Artifact = "owner:" + services.GetRequiredService<InvokedRun>().UserKey,
                    Accepted = true,
                }
            ),
            InvokedStepModes.Fail => throw new InvalidOperationException("the invoked step failed"),
            InvokedStepModes.Chain => Task.FromResult(
                new InvokedStepOutput { Artifact = "chain:" + input.Note, Chain = true }
            ),
            InvokedStepModes.Unaccepted => Task.FromResult(
                new InvokedStepOutput { Artifact = "unaccepted:" + input.Note }
            ),
            InvokedStepModes.Big => Task.FromResult(
                new InvokedStepOutput { Artifact = new string('b', 70 * 1024), Accepted = true }
            ),
            InvokedStepModes.Secret => Task.FromResult(
                new InvokedStepOutput
                {
                    Artifact = InvokedStepModes.Reversed(input.Note),
                    Accepted = true,
                }
            ),
            InvokedStepModes.Grow => Task.FromResult(
                new InvokedStepOutput { Artifact = new string('g', 40 * 1024), Accepted = true }
            ),
            _ => Task.FromResult(
                new InvokedStepOutput { Artifact = "artifact:" + input.Note, Accepted = true }
            ),
        };
}
