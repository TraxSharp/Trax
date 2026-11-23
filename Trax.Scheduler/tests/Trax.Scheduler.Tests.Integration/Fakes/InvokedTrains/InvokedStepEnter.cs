using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public sealed class InvokedStepEnter : EffectJunction<InvokedStepInput, InvokedStepGated>
{
    public override async Task<InvokedStepGated> Run(InvokedStepInput input)
    {
        if (string.Equals(input.Mode, InvokedStepModes.Gate, StringComparison.Ordinal))
        {
            InvokedStepGate.Enter();
            await InvokedStepGate.Released.WaitAsync(CancellationToken).ConfigureAwait(false);
        }

        return new InvokedStepGated(input.Mode, input.Note);
    }
}
