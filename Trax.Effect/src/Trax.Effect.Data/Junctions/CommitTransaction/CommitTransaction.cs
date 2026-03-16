using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Junctions.CommitTransaction;

/// <summary>
/// Built-in junction allowing for transactions to be committed.
/// </summary>
public class CommitTransaction(IDataContext dataContextFactory) : Junction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        await dataContextFactory.CommitTransaction();

        return Unit.Default;
    }
}
