using Trax.Effect.Data.Services.DataContext;
using Trax.Core.Step;
using LanguageExt;

namespace Trax.Effect.Data.Steps.BeginTransaction;

/// <summary>
/// Built-in step allowing for transactions to occur.
/// </summary>
public class BeginTransaction(IDataContext dataContext) : Step<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        await dataContext.BeginTransaction(CancellationToken);

        return Unit.Default;
    }
}
