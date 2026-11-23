using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.InvokedRunListener;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The fast path: on the host that ran an invoked train, delivers its outcome as soon as its terminal write has
/// committed, when this host registers the machine that invoked it. Runs outside the train's execution context, so
/// nothing ambient from the run (its log scope, its caller) reaches the delivery. Never throws: the reconciler's
/// sweep delivers whatever this misses.
/// </summary>
internal sealed class InvokeOutcomeHookFactory(InvokeOutcomeReconciler reconciler)
    : ITrainLifecycleHookFactory
{
    // Long enough for a delivery that queues the next run; a hook never holds the run that called it longer.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    public ITrainLifecycleHook Create() => new Hook(reconciler);

    private sealed class Hook(InvokeOutcomeReconciler reconciler) : ITrainLifecycleHook
    {
        public Task OnCompleted(Metadata metadata, CancellationToken ct) => Deliver(metadata);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            Deliver(metadata);

        public Task OnCancelled(Metadata metadata, CancellationToken ct) => Deliver(metadata);

        private async Task Deliver(Metadata metadata)
        {
            if (
                metadata.InvokingMachine is not { } machine
                || !reconciler.Handles(machine)
                || string.IsNullOrEmpty(metadata.ExternalId)
            )
                return;

            var token = metadata.ExternalId;
            try
            {
                using var bounded = new CancellationTokenSource(Bound);
                Task delivery;
                using (ExecutionContext.SuppressFlow())
                    delivery = Task.Run(() => reconciler.TryDeliver(token, bounded.Token));
                await delivery.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // TryDeliver logs its own failures; a bound that ran out leaves the run to the sweep.
            }
        }
    }
}
