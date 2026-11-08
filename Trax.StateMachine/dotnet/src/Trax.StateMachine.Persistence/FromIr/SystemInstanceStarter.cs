using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// Starts each system machine served from features at its fixed key when the host starts (ADR
/// <c>Trax.Docs/adr/0058</c>): no client may start one, and a view or a policy reaches it at its key. Starting is
/// idempotent, so every host of a cluster runs it and the first creates the instance. The key <c>shop/main</c> is the
/// instance of <c>shop</c> for <c>MachineKey.Of("main")</c>. <c>AddStateMachines</c> registers it after the startup
/// validators, whose <c>StartingAsync</c> run first, so a host they refuse writes no instance.
/// </summary>
internal sealed class SystemInstanceStarter(IServiceScopeFactory scopes, HostFile host)
    : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();
        foreach (
            var entry in host
                .Machines.Values.Where(m => m.SystemKey is not null)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
        )
        {
            var systemKey = entry.SystemKey!;
            var key = MachineKey.Of(systemKey[(systemKey.IndexOf('/') + 1)..]);
            await instances.Start(entry.Id, key, null, cancellationToken);
        }
    }

    /// <summary>The instance id a system machine's key gives it, as <see cref="StartingAsync"/> starts it.</summary>
    internal static Guid IdOf(string machine, string systemKey) =>
        MachineInstanceId.For(machine, MachineKey.Of(systemKey[(systemKey.IndexOf('/') + 1)..]));

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
