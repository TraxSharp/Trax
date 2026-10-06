using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Junctions.CommitTransaction;

/// <summary>
/// Built-in junction that commits the transaction a preceding <c>BeginTransaction</c> opened on the
/// run's scoped <see cref="IDataContext"/>.
/// </summary>
/// <remarks>
/// It commits only; it does not call <c>SaveChanges</c>, so entities still pending in the change
/// tracker are not written by this junction. Fails if no transaction is open on the context.
/// </remarks>
public class CommitTransaction(IDataContext dataContextFactory) : Junction<Unit, Unit>
{
    /// <summary>Commits the open transaction.</summary>
    /// <param name="input">Unused.</param>
    /// <returns><see cref="Unit.Default"/>.</returns>
    public override async Task<Unit> Run(Unit input)
    {
        await dataContextFactory.CommitTransaction();

        return Unit.Default;
    }
}
