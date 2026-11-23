using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// Publishes at once with a fresh receipt, or, while <see cref="ReminderPublishGate"/> is armed, holds the effect
/// (and so its claim) until the test releases it.
/// </summary>
public sealed class ReminderPublish : IReminderPublish
{
    public async Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (ReminderPublishGate.Armed)
        {
            ReminderPublishGate.Enter();
            await ReminderPublishGate.Released.ConfigureAwait(false);
        }

        return "published-" + Guid.NewGuid().ToString("N");
    }
}
