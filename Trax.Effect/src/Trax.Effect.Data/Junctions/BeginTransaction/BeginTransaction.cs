using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Effect.Data.Junctions.BeginTransaction;

/// <summary>
/// Built-in junction allowing for transactions to occur.
/// </summary>
public class BeginTransaction(IDataContext dataContext) : Junction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        await dataContext.BeginTransaction(CancellationToken);

        return Unit.Default;
    }
}
