using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>
/// Wraps the instance store so that every read of a row by its invoke token returns a concurrency token the row no
/// longer holds: as if the row changed between each read and its write, every time.
/// </summary>
internal sealed class StaleReadStore(IMachineInstanceStore inner) : IMachineInstanceStore
{
    /// <summary>Replaces the registered store with this wrapper around it.</summary>
    public static void Install(IServiceCollection services)
    {
        var registered = services.Last(d => d.ServiceType == typeof(IMachineInstanceStore));
        services.Remove(registered);
        var create = registered.ImplementationFactory!;
        services.AddScoped<IMachineInstanceStore>(sp => new StaleReadStore(
            (IMachineInstanceStore)create(sp)
        ));
    }

    public async Task<StoredInstance?> GetByInvokeToken(
        string invokeToken,
        CancellationToken cancellationToken = default
    ) =>
        await inner.GetByInvokeToken(invokeToken, cancellationToken) is { } row
            ? row with
            {
                Snapshot = row.Snapshot with { Token = Guid.NewGuid() },
            }
            : null;

    public Task<StoredSnapshot?> Get(
        DraftOwner owner,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) => inner.Get(owner, machine, id, cancellationToken);

    public Task<bool> Insert(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        string? invokeToken,
        CancellationToken cancellationToken = default
    ) => inner.Insert(owner, id, snapshot, invokeToken, cancellationToken);

    public Task<bool> DeleteHolding(
        DraftOwner owner,
        string machine,
        Guid id,
        string? invokeToken,
        CancellationToken cancellationToken = default
    ) => inner.DeleteHolding(owner, machine, id, invokeToken, cancellationToken);

    public Task<bool> Update(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        InvokeTokenWrite? invokeToken = null,
        CancellationToken cancellationToken = default
    ) => inner.Update(owner, id, snapshot, expectedToken, request, invokeToken, cancellationToken);

    public Task<bool> SetInvokeToken(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid expectedToken,
        InvokeTokenWrite invokeToken,
        CancellationToken cancellationToken = default
    ) => inner.SetInvokeToken(owner, machine, id, expectedToken, invokeToken, cancellationToken);

    public Task<bool> ApplyByInvokeToken(
        string invokeToken,
        Snapshot snapshot,
        string? nextInvokeToken,
        Guid? expectedToken = null,
        CancellationToken cancellationToken = default
    ) =>
        inner.ApplyByInvokeToken(
            invokeToken,
            snapshot,
            nextInvokeToken,
            expectedToken,
            cancellationToken
        );

    public Task<bool> StrandByInvokeToken(
        string invokeToken,
        Snapshot snapshot,
        Guid expectedToken,
        CancellationToken cancellationToken = default
    ) => inner.StrandByInvokeToken(invokeToken, snapshot, expectedToken, cancellationToken);
}
