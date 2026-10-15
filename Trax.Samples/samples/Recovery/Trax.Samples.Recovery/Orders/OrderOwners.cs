using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.Orders;

/// <summary>
/// Whose order a delivery run is: the user who owns the <c>order</c> instance whose <c>Ordered</c> state invoked it.
/// The run's input is built from the cart alone, so the owner is read from the run's record.
/// </summary>
public sealed class OrderOwners(IDataContextProviderFactory dataContexts)
{
    /// <summary>
    /// The user key of the order the run <paramref name="metadataId"/> delivers, or null when the run was not invoked
    /// by an order or its owner is not one user.
    /// </summary>
    public async Task<string?> OwnerOfRun(long metadataId, CancellationToken cancellationToken)
    {
        await using var context = await dataContexts.CreateDbContextAsync(cancellationToken);
        var run = await context
            .Metadatas.AsNoTracking()
            .Where(m => m.Id == metadataId)
            .Select(m => new { m.InvokingMachine, m.InvokingInstanceId })
            .FirstOrDefaultAsync(cancellationToken);
        if (
            run is not { InvokingMachine: OrderMachine.MachineId, InvokingInstanceId: { } instance }
        )
            return null;

        // An instance id is unique per user only, so two owners of one id are nobody's.
        var owners = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.Machine == OrderMachine.MachineId
                && d.Id == instance
                && d.OwnerKind == SnapshotOwnerKind.User
            )
            .Select(d => d.UserKey)
            .Distinct()
            .Take(2)
            .ToListAsync(cancellationToken);
        return owners is [{ } only] ? only : null;
    }
}
