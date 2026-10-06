using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Junctions.BeginTransaction;

/// <summary>
/// Built-in junction that opens a database transaction on the run's scoped <see cref="IDataContext"/>.
/// Place it before the junctions whose writes must be atomic and follow them with
/// <c>CommitTransaction</c>.
/// </summary>
/// <remarks>
/// The transaction runs at the database's default isolation level and belongs to the scoped
/// context, so only writes made
/// through that same <see cref="IDataContext"/> instance are inside it. This junction does not roll
/// back on failure: if a later junction fails, the transaction stays open until the context is
/// disposed, which discards it.
/// </remarks>
public class BeginTransaction(IDataContext dataContext) : Junction<Unit, Unit>
{
    /// <summary>Begins the transaction, honouring the junction's cancellation token.</summary>
    /// <param name="input">Unused.</param>
    /// <returns><see cref="Unit.Default"/>.</returns>
    public override async Task<Unit> Run(Unit input)
    {
        await dataContext.BeginTransaction(CancellationToken);

        return Unit.Default;
    }
}
