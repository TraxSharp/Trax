using System.Data.Common;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>What a timer test registers on an <see cref="InvokeCluster"/> host, beyond the cluster's defaults.</summary>
internal static class TimerHosts
{
    /// <summary>Registers the reminder's effect, which every host that runs a reminder's <c>Publish</c> needs.</summary>
    public static void Publishing(IServiceCollection services) =>
        services.AddSingleton<IReminderPublish, ReminderPublish>();

    /// <summary>Keeps only <paramref name="machines"/> of the machines the assembly scan registered.</summary>
    public static Action<IServiceCollection> Only(params Type[] machines) =>
        services =>
        {
            foreach (
                var descriptor in services
                    .Where(d =>
                        d.ServiceType == typeof(IMachine)
                        && !machines.Contains(d.ImplementationType)
                    )
                    .ToList()
            )
                services.Remove(descriptor);
        };

    /// <summary>Drops <paramref name="machines"/> from the machines the assembly scan registered.</summary>
    public static Action<IServiceCollection> Without(params Type[] machines) =>
        services =>
        {
            foreach (
                var descriptor in services
                    .Where(d =>
                        d.ServiceType == typeof(IMachine) && machines.Contains(d.ImplementationType)
                    )
                    .ToList()
            )
                services.Remove(descriptor);
        };

    /// <summary>Runs the reminder as <paramref name="shape"/> on this host instead of version 1.</summary>
    public static Action<IServiceCollection> Deploy(ReminderShape shape) =>
        services =>
        {
            Without(typeof(ReminderMachine))(services);
            services.AddSingleton<IMachine>(new ReminderMachine(shape));
        };

    /// <summary>Holds the next draft write on this host while <paramref name="gate"/> is armed.</summary>
    public static Action<IServiceCollection> Pausing(PauseGate gate) =>
        services => PausingSnapshotStore.Install(services, gate);

    /// <summary>Every one of <paramref name="parts"/>, in order.</summary>
    public static Action<IServiceCollection> All(params Action<IServiceCollection>[] parts) =>
        services =>
        {
            foreach (var part in parts)
                part(services);
        };
}
